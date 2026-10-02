from __future__ import annotations
from .class_phase184_foxglove_desktop_live_acceptance_tests_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveDesktopLiveAcceptanceTests_runtime:
    """Decomposed Phase192 implementation component."""
    def test_desktop_process_identity_is_bound_before_lease_release(self):
        """Verify desktop process identity is bound before lease release."""

        with temporary_directory("desktop-live-identity-bind-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="desktop-identity-mismatch",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(
            live_protocol.FAIL_DESKTOP_IDENTITY,
            summary["verdict"],
        )
        self.assertIn("job:close", harness.events)
        self.assertIn("desktop-lease:close", harness.events)
        self.assertLess(
            harness.events.index("launch:desktop"),
            harness.events.index("desktop-lease:close"),
        )
    def test_desktop_lease_baseexception_releases_before_cleanup_finishes(self):
        """Verify desktop lease baseexception releases before cleanup finishes."""

        with temporary_directory("desktop-live-lease-interrupt-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="desktop-lease-interrupt",
            )

            with self.assertRaises(KeyboardInterrupt):
                coordinator.run_acceptance(
                    harness.args(),
                    dependencies=harness.dependencies(),
                )

        self.assertFalse(harness.desktop_lease_active)
        self.assertIn("desktop-lease:close", harness.events)
        self.assertIn("job:close", harness.events)
    def test_success_launches_desktop_while_production_shaped_lease_is_active(self):
        """Verify success launches desktop while production shaped lease is active."""

        with temporary_directory("desktop-live-lease-shape-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual("PASS", summary["verdict"])
        self.assertIn("desktop:launch-lease-active", harness.events)
        dependency_fields = {
            field.name
            for field in dataclasses.fields(
                coordinator.CoordinatorDependencies
            )
        }
        self.assertIn(
            "desktop_executable_lease_factory",
            dependency_fields,
        )
        self.assertIs(
            cli_install.WindowsExecutableLease,
            coordinator._default_dependencies().desktop_executable_lease_factory,
        )
    def test_root_absence_and_unstable_zero_state_fail_before_desktop_proof(self):
        """Verify root absence and unstable zero state fail before desktop proof."""

        expected = {
            "root-absent": live_protocol.FAIL_DESKTOP_IDENTITY,
            "unstable-initial": live_protocol.FAIL_DESKTOP_CONNECTION,
        }
        for mode, failure_code in expected.items():
            with self.subTest(mode=mode):
                with temporary_directory(
                    f"desktop-live-proof-{mode}-"
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
                self.assertNotIn("desktop:close-request", harness.events)
                if mode == "unstable-initial":
                    self.assertNotIn(
                        "desktop",
                        [kind for kind, _record in harness.launches],
                    )
    def test_raw_context_order_and_transport_regression_never_pass(self):
        """Verify raw context order and transport regression never pass."""

        expected = {
            "context-after-initial": False,
            "transport-regression": True,
        }
        for mode, desktop_started in expected.items():
            with self.subTest(mode=mode):
                with temporary_directory(
                    f"desktop-live-chronology-{mode}-"
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
                    live_protocol.FAIL_DESKTOP_CONNECTION,
                    summary["verdict"],
                )
                self.assertEqual(
                    desktop_started,
                    any(
                        kind == "desktop"
                        for kind, _record in harness.launches
                    ),
                )
    def test_base_summary_is_validated_before_desktop_close(self):
        """Verify base summary is validated before desktop close."""

        with temporary_directory("desktop-live-summary-order-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertLess(
            harness.events.index("json:read-summary"),
            harness.events.index("desktop:close-request"),
        )
    def test_barrier_written_only_after_first_client_and_second_follows_barrier(self):
        """Verify barrier written only after first client and second follows barrier."""

        with temporary_directory("desktop-live-barrier-order-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        connection = summary["connection"]
        self.assertLess(
            connection["desktopIdentityCapturedAt"],
            connection["firstObservedAt"],
        )
        self.assertLess(
            connection["firstObservedAt"],
            connection["barrierWrittenAt"],
        )
        self.assertLess(
            connection["barrierWrittenAt"],
            connection["secondObservedAt"],
        )
        self.assertGreater(
            harness.events.index("barrier:write"),
            harness.events.index("launch:desktop"),
        )
    def test_failure_after_barrier_still_closes_job_and_removes_only_owned_barrier(self):
        """Verify failure after barrier still closes job and removes only owned barrier."""

        with temporary_directory("desktop-live-failure-cleanup-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="base-summary-fail",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )
            barrier_exists = harness.barrier.exists()

        self.assertEqual(summary["verdict"], live_protocol.FAIL_EVIDENCE)
        self.assertFalse(barrier_exists)
        self.assertTrue(summary["cleanup"]["jobClosed"])
        self.assertTrue(summary["connection"]["barrierRemoved"])
        self.assertTrue(summary["cleanup"]["barrier"])
        self.assertNotIn("desktop:close-request", harness.events)
    def test_cleanup_port_failure_prevents_pass(self):
        """Verify cleanup port failure prevents pass."""

        with temporary_directory("desktop-live-port-busy-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="cleanup-port-busy",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], live_protocol.FAIL_CLEANUP)
        self.assertFalse(summary["cleanup"]["port"])
    def test_main_returns_zero_only_for_validated_pass(self):
        """Verify main returns zero only for validated pass."""

        with temporary_directory("desktop-live-main-pass-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))
            argv = [
                "--unity-editor",
                str(harness.unity),
                "--foxglove-cli",
                str(harness.cli),
                "--desktop-executable",
                str(harness.desktop),
                "--cli-receipt",
                str(harness.receipt),
                "--foxglove-port",
                str(harness.port),
                "--run-id",
                harness.run_id,
            ]
            self.assertEqual(
                coordinator.main(argv, dependencies=harness.dependencies()),
                0,
            )

        with temporary_directory("desktop-live-main-fail-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="wrong-token",
            )
            argv = [
                "--unity-editor",
                str(harness.unity),
                "--foxglove-cli",
                str(harness.cli),
                "--desktop-executable",
                str(harness.desktop),
                "--cli-receipt",
                str(harness.receipt),
                "--foxglove-port",
                str(harness.port),
                "--run-id",
                harness.run_id,
            ]
            self.assertEqual(
                coordinator.main(argv, dependencies=harness.dependencies()),
                1,
            )
class Phase184FoxgloveDesktopLiveAcceptanceTests(_Phase184FoxgloveDesktopLiveAcceptanceTests_support, _Phase184FoxgloveDesktopLiveAcceptanceTests_fixtures, _Phase184FoxgloveDesktopLiveAcceptanceTests_validation, _Phase184FoxgloveDesktopLiveAcceptanceTests_runtime, unittest.TestCase):
    """Lock the coordinator into the focused, owned Windows live route."""
    pass


__all__ = [name for name in globals() if not name.startswith("__")]
