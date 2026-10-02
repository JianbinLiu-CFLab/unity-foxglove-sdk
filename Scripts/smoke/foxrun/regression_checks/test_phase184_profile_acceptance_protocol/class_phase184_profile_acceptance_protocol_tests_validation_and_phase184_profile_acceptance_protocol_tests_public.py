from __future__ import annotations
from .class_phase184_profile_acceptance_protocol_tests_fixtures import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceProtocolTests_validation:
    """Decomposed Phase192 implementation component."""
    def test_progress_watchdog_uses_last_progress_age_not_total_duration(self):
        """A long progressing build survives while a silent operation fails."""

        protocol = load_protocol_module()
        clock = [0.0]
        watchdog = protocol.ProgressWatchdog(
            "build",
            stall_seconds=10.0,
            now=lambda: clock[0],
        )
        clock[0] = 9.0
        watchdog.check()
        watchdog.progress("colcon output")
        clock[0] = 18.0
        watchdog.check()
        clock[0] = 20.1
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_BUILD_STALLED"):
            watchdog.check()
    def test_operation_watchdog_defaults_preserve_unity_and_cold_build_ceilings(self):
        """Established no-progress ceilings remain explicit protocol values."""

        protocol = load_protocol_module()
        self.assertEqual(900, protocol.OPERATION_STALL_SECONDS["runtime-selection"])
        self.assertEqual(1800, protocol.OPERATION_STALL_SECONDS["build"])
        self.assertEqual(30, protocol.OPERATION_STALL_SECONDS["teardown"])
    def test_process_group_options_are_platform_specific_and_argument_array_safe(self):
        """Windows and POSIX children receive owned group construction options."""

        protocol = load_protocol_module()
        windows = protocol.subprocess_group_options("nt")
        posix = protocol.subprocess_group_options("posix")

        self.assertNotEqual(0, windows["creationflags"])
        self.assertFalse(windows["start_new_session"])
        self.assertEqual(0, posix["creationflags"])
        self.assertTrue(posix["start_new_session"])
        with self.assertRaises(ValueError):
            protocol.subprocess_group_options("unknown")
class Phase184ProfileAcceptanceProtocolTests(_Phase184ProfileAcceptanceProtocolTests_support, _Phase184ProfileAcceptanceProtocolTests_fixtures, _Phase184ProfileAcceptanceProtocolTests_validation, unittest.TestCase):
    """Reject incomplete, contradictory, stale, or unsafe Phase184-G evidence."""
    pass


__all__ = [name for name in globals() if not name.startswith("__")]
