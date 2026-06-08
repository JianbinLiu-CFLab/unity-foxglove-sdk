#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for core smoke-script failure handling.

from __future__ import annotations

import asyncio
import importlib.util
import os
import ssl
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SMOKE = ROOT / "Scripts" / "smoke"


def load_smoke_module(name: str, relative: str):
    """Load one smoke helper with Scripts/smoke on sys.path for sibling imports."""
    path = SMOKE / relative
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    original_path = list(sys.path)
    sys.path.insert(0, str(SMOKE))
    try:
        spec.loader.exec_module(module)
    finally:
        sys.path[:] = original_path
    return module


class NeverAdvertisesWebSocket:
    """Fake websocket whose receive call outlives the advertise timeout."""

    async def recv(self):
        """Sleep longer than any test advertise timeout."""
        await asyncio.sleep(60)


class CoreSmokeScriptTests(unittest.TestCase):
    """Regression coverage for local smoke helper edge cases."""

    def test_topic_waiters_raise_topic_not_found_on_advertise_timeout(self) -> None:
        """Topic probes should return their structured timeout verdict path."""
        cases = [
            ("topic_rate_probe_under_test", "topic_rate_probe.py"),
            ("pointcloud_qos_probe_under_test", "pointcloud_qos_probe.py"),
            ("compressed_pointcloud_draco_probe_under_test", "compressed_pointcloud_draco_probe.py"),
        ]

        for name, relative in cases:
            with self.subTest(relative=relative):
                module = load_smoke_module(name, relative)
                with self.assertRaises(module.TopicNotFoundError):
                    asyncio.run(module.wait_for_channel(NeverAdvertisesWebSocket(), "/missing", 0.01))

    def test_phase139b_launch_backend_enforces_startup_timeout_without_stdout(self) -> None:
        """A silent child process should not block past startup_timeout."""
        module = load_smoke_module("phase139b_under_test", "phase139b_remote_data_loader_acceptance.py")

        class BlockingStdout:
            """Stdout stub whose readline blocks like a quiet child process."""

            def readline(self):
                """Block briefly before returning no line."""
                time.sleep(0.25)
                return ""

        class SilentProcess:
            """Process stub that stays alive and never emits startup output."""

            stdout = BlockingStdout()

            def poll(self):
                """Report that the process is still running."""
                return None

            def terminate(self):
                """Record termination requested by cleanup."""
                self.terminated = True

        args = SimpleNamespace(
            mcap="input.mcap",
            host="127.0.0.1",
            port=0,
            source_id="phase139b-source",
            name="phase139b",
            max_data_bytes=None,
            token="",
            startup_timeout=0.02,
        )

        with mock.patch.object(module.subprocess, "Popen", return_value=SilentProcess()):
            started = time.monotonic()
            with self.assertRaises(RuntimeError):
                module.launch_backend(args, ROOT)
            elapsed = time.monotonic() - started

        self.assertLess(elapsed, 0.12)

    def test_phase139b_windows_stop_backend_does_not_raise_on_wait_timeout(self) -> None:
        """Windows cleanup should not mask the original smoke result."""
        module = load_smoke_module("phase139b_stop_under_test", "phase139b_remote_data_loader_acceptance.py")

        class SlowProcess:
            """Process stub whose first wait times out before kill."""

            pid = 12345

            def __init__(self):
                """Initialize kill and wait tracking."""
                self.killed = False
                self.waits = 0

            def poll(self):
                """Report that the process is still running."""
                return None

            def wait(self, timeout=None):
                """Timeout before kill and succeed after kill."""
                self.waits += 1
                if not self.killed:
                    raise subprocess.TimeoutExpired(["fake"], timeout)
                return 0

            def kill(self):
                """Record that the process was force-killed."""
                self.killed = True

        process = SlowProcess()
        with mock.patch.object(module.os, "name", "nt"):
            with mock.patch.object(module.subprocess, "run"):
                module.stop_backend(process)

        self.assertTrue(process.killed)

    def test_compressed_mcap_inspector_reports_unsupported_compression_first(self) -> None:
        """Compressed chunks should produce an unsupported-compression verdict, not Draco failure noise."""
        module = load_smoke_module("compressed_mcap_under_test", "compressed_pointcloud_mcap_inspect.py")
        parsed = module.ParsedMcap(schemas={}, channels={}, messages=[], unsupported_chunks=1)

        ok, lines = module.inspect_mcap(parsed, module.RAW_TOPIC, module.COMPRESSED_TOPIC)

        self.assertFalse(ok)
        self.assertIn("unsupported", lines[0].lower())
        self.assertFalse(any("no compressed payload decoded" in line for line in lines))

    def test_phase139_e2e_supports_insecure_wss_context(self) -> None:
        """The e2e helper should support local self-signed WSS smoke tests."""
        module = load_smoke_module("phase139_e2e_under_test", "phase139_e2e_integration_smoke.py")
        args = module.build_parser().parse_args(["--url", "wss://127.0.0.1:8765", "--insecure"])

        context = module.build_ssl_context(args.url, args.insecure)

        self.assertTrue(args.insecure)
        self.assertIsNotNone(context)
        self.assertFalse(context.check_hostname)
        self.assertEqual(ssl.CERT_NONE, context.verify_mode)

    def test_phase34_fixture_constants_are_checked_without_assert(self) -> None:
        """Optimized Python should still enforce load-bearing fixture constants."""
        module = load_smoke_module("phase34_under_test", "phase34_attachment_mcap.py")

        with mock.patch.object(module, "CHANNEL_ID", 2):
            with self.assertRaises(ValueError):
                module.validate_fixture_constants()

    def test_phase110_import_does_not_exit_when_ros2_env_helper_is_missing(self) -> None:
        """Importing the helper as a module should not terminate a composite runner."""
        path = SMOKE / "phase110_string_smoke_acceptance.py"
        spec = importlib.util.spec_from_file_location("phase110_import_under_test", path)
        module = importlib.util.module_from_spec(spec)
        assert spec.loader is not None
        original_path = list(sys.path)
        sys.path = [entry for entry in sys.path if Path(entry or os.getcwd()).resolve() != SMOKE.resolve()]
        try:
            spec.loader.exec_module(module)
        finally:
            sys.path[:] = original_path

        self.assertTrue(hasattr(module, "main"))


if __name__ == "__main__":
    unittest.main()
