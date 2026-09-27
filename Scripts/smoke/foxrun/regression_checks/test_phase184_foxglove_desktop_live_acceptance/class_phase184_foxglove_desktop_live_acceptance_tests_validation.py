from __future__ import annotations
from .class_phase184_foxglove_desktop_live_acceptance_tests_fixtures import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveDesktopLiveAcceptanceTests_validation:
    def test_connection_window_starts_after_current_client_readiness(self):
        """Verify connection window starts after current client readiness."""

        with temporary_directory("desktop-live-delayed-ready-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="delayed-base-ready",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], "PASS")
        self.assertIn("base:ready", harness.events)
        self.assertLess(
            harness.events.index("base:ready"),
            harness.events.index("marker:context"),
        )
        self.assertGreater(
            harness.clock.value,
            100.0 + coordinator.CONNECTION_TIMEOUT_SECONDS,
        )
    def test_context_readiness_uses_cold_start_budget_before_connection_window(self):
        """Verify a bounded cold Unity start does not consume connection time."""

        with temporary_directory("desktop-live-delayed-context-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="delayed-context",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], "PASS")
        self.assertIn("marker:context", harness.events)
        self.assertGreater(
            harness.clock.value,
            100.0 + coordinator.CONNECTION_TIMEOUT_SECONDS,
        )
    def test_base_readiness_is_required_and_exactly_correlated(self):
        """Verify base readiness is required and exactly correlated."""

        for mode in (
            "missing-base-ready",
            "stale-base-ready",
            "malformed-base-ready",
            "boolean-base-ready-schema",
            "base-ready-path-error",
        ):
            with self.subTest(mode=mode):
                with temporary_directory(
                    f"desktop-live-{mode}-"
                ) as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(
                    summary["verdict"],
                    live_protocol.FAIL_FOXRUN_CHILD,
                )
                self.assertNotIn(
                    "desktop",
                    [kind for kind, _record in harness.launches],
                )
    def test_success_records_graceful_forced_cleanup_and_removes_barrier(self):
        """Verify success records graceful forced cleanup and removes barrier."""

        with temporary_directory("desktop-live-cleanup-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

            self.assertFalse(harness.barrier.exists())

        self.assertTrue(summary["cleanup"]["jobClosed"])
        self.assertTrue(summary["cleanup"]["port"])
        self.assertTrue(summary["cleanup"]["barrier"])
        self.assertEqual(
            [item["pid"] for item in summary["cleanup"]["gracefulOwnedIdentities"]],
            [202],
        )
        self.assertEqual(
            [item["pid"] for item in summary["cleanup"]["forcedOwnedIdentities"]],
            [203],
        )
        self.assertEqual(
            {
                item["pid"]
                for item in summary["cleanup"]["exitedOwnedIdentities"]
            },
            {
                item["pid"]
                for item in summary["desktop"]["ownedMemberIdentities"]
            },
        )
        self.assertEqual(
            summary["cleanup"]["residualOwnedIdentities"],
            [],
        )
        self.assertEqual(summary["desktop"]["externalIdentities"], [])
    def test_all_captured_members_are_identity_verified_after_job_close(self):
        """Verify all captured members are identity verified after job close."""

        with temporary_directory("desktop-live-exit-proof-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], "PASS")
        self.assertEqual(len(harness.exit_verifier_inputs), 1)
        self.assertLess(
            harness.events.index("job:close"),
            harness.events.index("identity-exit:verify"),
        )
        captured = {
            (
                identity.pid,
                identity.creation_time_100ns,
                identity.executable,
            )
            for identity in harness.exit_verifier_inputs[0]
        }
        summarized = {
            (
                item["pid"],
                item["creationTime100ns"],
                item["executable"],
            )
            for item in summary["desktop"]["ownedMemberIdentities"]
        }
        self.assertEqual(captured, summarized)
    def test_residual_owned_member_forces_cleanup_failure(self):
        """Verify residual owned member forces cleanup failure."""

        with temporary_directory("desktop-live-residual-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="residual-owned-process",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], live_protocol.FAIL_CLEANUP)
        self.assertFalse(summary["cleanup"]["processes"])
        self.assertEqual(
            len(summary["cleanup"]["residualOwnedIdentities"]),
            1,
        )
    def test_late_spawn_is_refreshed_recorded_and_exit_verified(self):
        """Verify late spawn is refreshed recorded and exit verified."""

        with temporary_directory("desktop-live-late-spawn-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="late-spawn",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], "PASS")
        owned_pids = {
            item["pid"]
            for item in summary["desktop"]["ownedMemberIdentities"]
        }
        self.assertIn(303, owned_pids)
        self.assertIn(
            303,
            {
                item["pid"]
                for item in summary["cleanup"]["exitedOwnedIdentities"]
            },
        )
        self.assertGreaterEqual(harness.events.count("job:members"), 3)
        close_index = harness.events.index("desktop:close-request")
        final_member_index = max(
            index
            for index, event in enumerate(harness.events)
            if event == "job:members"
        )
        self.assertLess(final_member_index, close_index)
        self.assertGreater(
            final_member_index,
            harness.events.index("json:read-summary"),
        )
    def test_preexisting_exact_path_process_rejects_before_any_root(self):
        """Verify preexisting exact path process rejects before any root."""

        with temporary_directory("desktop-live-preexisting-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="preexisting",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(
            summary["verdict"],
            live_protocol.FAIL_DESKTOP_PREFLIGHT,
        )
        self.assertEqual(harness.launches, [])
        self.assertNotIn("desktop:close-request", harness.events)
        self.assertIn("job:close", harness.events)
        self.assertEqual(
            [item["pid"] for item in summary["desktop"]["externalIdentities"]],
            [404],
        )
    def test_external_single_instance_and_close_races_are_never_close_targets(self):
        """Verify external single instance and close races are never close targets."""

        for mode in (
            "external-after-desktop",
            "late-external",
            "external-during-close",
        ):
            with self.subTest(mode=mode):
                with temporary_directory(f"desktop-live-{mode}-") as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(
                    summary["verdict"],
                    live_protocol.FAIL_DESKTOP_IDENTITY,
                )
                self.assertEqual(
                    [item["pid"] for item in summary["desktop"]["externalIdentities"]],
                    [404],
                )
                self.assertIn("job:close", harness.events)
                if mode in {
                    "external-after-desktop",
                    "late-external",
                }:
                    self.assertNotIn("desktop:close-request", harness.events)
    def test_transport_and_child_failure_modes_have_stable_terminal_classes(self):
        """Verify transport and child failure modes have stable terminal classes."""

        expected = {
            "cli-fail": live_protocol.FAIL_CLI_PROVENANCE,
            "job-create-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "busy-port": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-version-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-hash-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-handler-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-start-fail": live_protocol.FAIL_DESKTOP_START,
            "root-absent": live_protocol.FAIL_DESKTOP_IDENTITY,
            "unstable-initial": live_protocol.FAIL_DESKTOP_CONNECTION,
            "context-after-initial": (
                live_protocol.FAIL_DESKTOP_CONNECTION
            ),
            "transport-regression": (
                live_protocol.FAIL_DESKTOP_CONNECTION
            ),
            "wrong-token": live_protocol.FAIL_DESKTOP_CONNECTION,
            "wrong-case": live_protocol.FAIL_DESKTOP_CONNECTION,
            "wrong-order": live_protocol.FAIL_DESKTOP_CONNECTION,
            "overflow": live_protocol.FAIL_DESKTOP_CONNECTION,
            "coordinator-log-overflow": (
                live_protocol.FAIL_DESKTOP_CONNECTION
            ),
            "missing-context": live_protocol.FAIL_DESKTOP_CONNECTION,
            "missing-first": live_protocol.FAIL_DESKTOP_CONNECTION,
            "base-exit-nonzero": live_protocol.FAIL_FOXRUN_CHILD,
            "base-summary-fail": live_protocol.FAIL_EVIDENCE,
        }
        for mode, failure_code in expected.items():
            with self.subTest(mode=mode):
                with temporary_directory(f"desktop-live-{mode}-") as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(summary["verdict"], failure_code)
                self.assertEqual(len(harness.summary_writes), 1)
                self.assertNotIn(
                    harness.token,
                    json.dumps(summary, sort_keys=True),
                )
    def test_job_create_and_desktop_preflight_failures_never_launch_desktop(self):
        """Verify job create and desktop preflight failures never launch desktop."""

        expected = {
            "job-create-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "busy-port": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-version-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-hash-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-handler-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
        }
        for mode, failure_code in expected.items():
            with self.subTest(mode=mode):
                with temporary_directory(
                    f"desktop-live-preflight-{mode}-"
                ) as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(summary["verdict"], failure_code)
                self.assertNotIn(
                    "desktop",
                    [kind for kind, _record in harness.launches],
                )
    def test_desktop_executable_reparse_and_updater_races_fail_closed(self):
        """Verify desktop executable reparse and updater races fail closed."""

        expected = {
            "desktop-reparse": (
                live_protocol.FAIL_DESKTOP_PREFLIGHT,
                False,
            ),
            "desktop-version-race": (
                live_protocol.FAIL_DESKTOP_PREFLIGHT,
                False,
            ),
            "desktop-handler-race": (
                live_protocol.FAIL_DESKTOP_PREFLIGHT,
                False,
            ),
            "desktop-updater-before-launch": (
                live_protocol.FAIL_DESKTOP_IDENTITY,
                False,
            ),
            "desktop-updater-after-launch": (
                live_protocol.FAIL_DESKTOP_IDENTITY,
                True,
            ),
        }
        for mode, (failure_code, desktop_started) in expected.items():
            with self.subTest(mode=mode):
                with temporary_directory(
                    f"desktop-live-integrity-{mode}-"
                ) as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(failure_code, summary["verdict"])
                self.assertEqual(
                    desktop_started,
                    any(
                        kind == "desktop"
                        for kind, _record in harness.launches
                    ),
                )
                self.assertNotEqual("PASS", summary["verdict"])
                self.assertFalse(harness.desktop_lease_active)
                if mode != "desktop-reparse":
                    self.assertIn("desktop-lease:close", harness.events)


__all__ = [name for name in globals() if not name.startswith("__")]
