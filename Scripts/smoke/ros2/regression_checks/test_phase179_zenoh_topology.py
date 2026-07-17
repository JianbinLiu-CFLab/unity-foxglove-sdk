#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for Phase179 Zenoh topology ownership and readiness policy.

"""Keep Phase179 Zenoh topology selection explicit, bounded, and transport-specific."""

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[4]
TOPOLOGY_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase179_zenoh_topology.py"


def load_topology_module():
    """Load the helper without requiring an installed ROS2 runtime."""

    smoke_dir = str(TOPOLOGY_PATH.parent)
    if smoke_dir not in sys.path:
        sys.path.insert(0, smoke_dir)
    spec = importlib.util.spec_from_file_location("phase179_zenoh_topology", TOPOLOGY_PATH)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class Phase179ZenohTopologyTests(unittest.TestCase):
    """Lock explicit router ownership before any process is launched."""

    def setUp(self) -> None:
        """Load a fresh module for isolated option-policy tests."""

        self.topology = load_topology_module()

    def test_zenoh_requires_one_explicit_topology_mode_and_safe_identity(self) -> None:
        """Zenoh may not silently invent a router or accept an unsafe topology id."""

        with self.assertRaises(self.topology.ZenohTopologyError) as missing:
            self.topology.validate_topology_options(
                "rmw_zenoh_cpp",
                router=None,
                no_router=False,
                topology_id="phase179-lyrical-zenoh",
            )
        self.assertEqual("ENVIRONMENT", missing.exception.category)

        with self.assertRaises(ValueError):
            self.topology.validate_topology_options(
                "rmw_zenoh_cpp",
                router=Path("router.exe"),
                no_router=False,
                topology_id="contains whitespace",
            )

        with self.assertRaises(ValueError):
            self.topology.validate_topology_options(
                "rmw_zenoh_cpp",
                router=Path("router.exe"),
                no_router=True,
                topology_id="phase179-lyrical-zenoh",
            )

    def test_topology_mode_distinguishes_owned_router_config_and_external_certification(self) -> None:
        """The summary-facing mode must identify who owns the router lifecycle."""

        owned = self.topology.validate_topology_options(
            "rmw_zenoh_cpp",
            router=Path("rmw_zenohd.exe"),
            no_router=False,
            topology_id="phase179-lyrical-zenoh",
        )
        config = self.topology.validate_topology_options(
            "rmw_zenoh_cpp",
            router=Path("topology.json5"),
            no_router=False,
            topology_id="phase179-lyrical-zenoh",
        )
        external = self.topology.validate_topology_options(
            "rmw_zenoh_cpp",
            router=None,
            no_router=True,
            topology_id="phase179-lyrical-zenoh",
        )

        self.assertEqual("owned-router", owned.mode)
        self.assertEqual("external-session-config", config.mode)
        self.assertEqual("external-certified-topology", external.mode)
        self.assertEqual("phase179-lyrical-zenoh", external.topology_id)

    def test_fastdds_rejects_zenoh_only_arguments(self) -> None:
        """A FastDDS row must not accidentally create or label a Zenoh topology."""

        with self.assertRaises(ValueError):
            self.topology.validate_topology_options(
                "rmw_fastrtps_cpp",
                router=Path("rmw_zenohd.exe"),
                no_router=False,
                topology_id="phase179-lyrical-zenoh",
            )

        not_applicable = self.topology.validate_topology_options(
            "rmw_fastrtps_cpp",
            router=None,
            no_router=False,
            topology_id=None,
        )
        self.assertEqual("not-applicable", not_applicable.mode)

    def test_owned_router_waits_for_ready_marker_and_cleanup_keeps_external_topology_untouched(self) -> None:
        """Only a helper-owned router may be stopped, and only after readiness was observed."""

        class FakeProcess:
            """Minimal owned-router process double that remains alive until cleanup."""

            pid = 4242
            returncode = None

            def poll(self):
                """Report the current synthetic process state."""

                return self.returncode

            def wait(self, timeout=None):
                """Record successful owned-process cleanup."""

                self.returncode = 0
                return self.returncode

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            router = root / "rmw_zenohd.exe"
            router.write_text("placeholder", encoding="utf-8")
            log_path = root / "router.log"
            fake_process = FakeProcess()

            def fake_popen(_command, **kwargs):
                """Emit the required router marker into the helper-owned log."""

                kwargs["stdout"].write("Started local Zenoh router\n")
                kwargs["stdout"].flush()
                return fake_process

            options = self.topology.validate_topology_options(
                "rmw_zenoh_cpp",
                router=router,
                no_router=False,
                topology_id="phase179-lyrical-zenoh",
            )
            with mock.patch.object(self.topology.os, "name", "nt"), mock.patch.object(
                self.topology.subprocess, "Popen", side_effect=fake_popen
            ), mock.patch.object(self.topology.subprocess, "run") as taskkill:
                handle = self.topology.start_topology(
                    options,
                    env={},
                    cwd=root,
                    log_path=log_path,
                    ready_timeout_seconds=0.1,
                )
                self.assertEqual("owned-router-ready", handle.readiness)
                self.assertIs(fake_process, handle.process)
                self.topology.close_topology(handle)

            taskkill.assert_called_once_with(
                ["taskkill", "/PID", "4242", "/T", "/F"],
                stdout=self.topology.subprocess.DEVNULL,
                stderr=self.topology.subprocess.DEVNULL,
                check=False,
            )
            self.assertTrue(log_path.is_file())

        external = self.topology.validate_topology_options(
            "rmw_zenoh_cpp",
            router=None,
            no_router=True,
            topology_id="phase179-lyrical-zenoh",
        )
        handle = self.topology.start_topology(
            external,
            env={},
            cwd=Path.cwd(),
            log_path=Path.cwd() / "not-created.log",
            ready_timeout_seconds=0.1,
        )
        self.assertIsNone(handle.process)
        self.topology.close_topology(handle)

    def test_owned_router_that_exits_immediately_after_its_ready_marker_is_rejected(self) -> None:
        """A stale or buffered `Started` line cannot make a dead router eligible for graph probing."""

        class ExitedProcess:
            """Synthetic router process that has already stopped once its marker is visible."""

            pid = 4243

            def poll(self):
                """Report the non-zero synthetic exit status."""

                return 7

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            router = root / "rmw_zenohd.exe"
            router.write_text("placeholder", encoding="utf-8")
            log_path = root / "router.log"

            def fake_popen(_command, **kwargs):
                """Write a misleading ready marker while returning an already exited process."""

                kwargs["stdout"].write("Started then stopped\n")
                kwargs["stdout"].flush()
                return ExitedProcess()

            options = self.topology.validate_topology_options(
                "rmw_zenoh_cpp",
                router=router,
                no_router=False,
                topology_id="phase179-lyrical-zenoh",
            )
            with mock.patch.object(self.topology.subprocess, "Popen", side_effect=fake_popen):
                with self.assertRaises(self.topology.ZenohTopologyError) as failure:
                    self.topology.start_topology(
                        options,
                        env={},
                        cwd=root,
                        log_path=log_path,
                        ready_timeout_seconds=0.1,
                    )

        self.assertEqual("ROUTER_EXITED", failure.exception.category)

    def test_cleanup_tolerates_a_posix_router_that_exits_before_its_process_group_is_signaled(self) -> None:
        """A router that exits during teardown must not require a fallback process API call."""

        class ExitedBetweenPollAndSignal:
            """Minimal Popen double that disappears after the initial liveness check."""

            pid = 4244

            def poll(self):
                """Appear live at the start of teardown so killpg is attempted."""

                return None

            def terminate(self):
                """Fail loudly if a stale POSIX process reaches the fallback path."""

                raise AssertionError("stale process must not receive terminate()")

        process = ExitedBetweenPollAndSignal()
        with mock.patch.object(self.topology.os, "name", "posix"), mock.patch.object(
            self.topology.os, "killpg", side_effect=ProcessLookupError(), create=True
        ):
            self.topology.terminate_owned_process(process)


if __name__ == "__main__":
    unittest.main()
