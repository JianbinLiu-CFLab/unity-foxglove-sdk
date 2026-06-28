#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for smoke helper failure handling.

from __future__ import annotations

import asyncio
import importlib.util
import json
import os
import ssl
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.error
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SMOKE = ROOT / "Scripts" / "smoke"


def load_smoke_module(name: str, relative: str):
    """Load one smoke helper with only its sibling directory on sys.path."""
    path = SMOKE / relative
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    original_path = list(sys.path)
    sys.path.insert(0, str(path.parent))
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


class AdvertisesMalformedAndValidChannel:
    """Fake websocket that first advertises malformed channels, then a valid one."""

    def __init__(self):
        """Initialize the scripted advertise frames."""
        self._frames = [
            json_bytes(
                {
                    "op": "advertise",
                    "channels": [
                        {"topic": "/tf"},
                        {"id": "not-an-int", "topic": "/tf"},
                        "bad-channel",
                    ],
                }
            ),
            json_bytes({"op": "advertise", "channels": [{"id": 7, "topic": "/tf"}]}),
        ]

    async def recv(self):
        """Return the next scripted frame."""
        if self._frames:
            return self._frames.pop(0)
        await asyncio.sleep(60)


def json_bytes(payload: dict) -> str:
    """Encode a JSON websocket text frame."""
    return json.dumps(payload)


class CoreSmokeScriptTests(unittest.TestCase):
    """Regression coverage for local smoke helper edge cases."""

    def test_topic_waiters_raise_topic_not_found_on_advertise_timeout(self) -> None:
        """Topic probes should return their structured timeout verdict path."""
        cases = [
            ("topic_rate_probe_under_test", "websocket/topic_rate_probe.py"),
            ("pointcloud_qos_probe_under_test", "websocket/pointcloud_qos_probe.py"),
            ("compressed_pointcloud_draco_probe_under_test", "websocket/compressed_pointcloud_draco_probe.py"),
        ]

        for name, relative in cases:
            with self.subTest(relative=relative):
                module = load_smoke_module(name, relative)
                with self.assertRaises(module.TopicNotFoundError):
                    asyncio.run(module.wait_for_channel(NeverAdvertisesWebSocket(), "/missing", 0.01))

    def test_phase139b_launch_backend_enforces_startup_timeout_without_stdout(self) -> None:
        """A silent child process should not block past startup_timeout."""
        module = load_smoke_module("phase139b_under_test", "replay/phase139b_remote_data_loader_acceptance.py")

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
        module = load_smoke_module("phase139b_stop_under_test", "replay/phase139b_remote_data_loader_acceptance.py")

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
        module = load_smoke_module("compressed_mcap_under_test", "mcap/compressed_pointcloud_mcap_inspect.py")
        parsed = module.ParsedMcap(schemas={}, channels={}, messages=[], unsupported_chunks=1)

        ok, lines = module.inspect_mcap(parsed, module.RAW_TOPIC, module.COMPRESSED_TOPIC)

        self.assertFalse(ok)
        self.assertIn("unsupported", lines[0].lower())
        self.assertFalse(any("no compressed payload decoded" in line for line in lines))

    def test_phase139_e2e_supports_insecure_wss_context(self) -> None:
        """The e2e helper should support local self-signed WSS smoke tests."""
        module = load_smoke_module("phase139_e2e_under_test", "replay/phase139_e2e_integration_smoke.py")
        args = module.build_parser().parse_args(["--url", "wss://127.0.0.1:8765", "--insecure"])

        context = module.build_ssl_context(args.url, args.insecure)

        self.assertTrue(args.insecure)
        self.assertIsNotNone(context)
        self.assertFalse(context.check_hostname)
        self.assertEqual(ssl.CERT_NONE, context.verify_mode)

    def test_phase139_e2e_skips_malformed_advertised_channels(self) -> None:
        """Malformed advertise channels should not crash the smoke helper."""
        module = load_smoke_module("phase139_e2e_malformed_ad_under_test", "replay/phase139_e2e_integration_smoke.py")

        channels = asyncio.run(module.collect_advertisements(AdvertisesMalformedAndValidChannel(), {"/tf"}, 0.5, 0.05))

        self.assertEqual([7], list(channels))
        self.assertEqual("/tf", channels[7]["topic"])

    def test_phase34_fixture_constants_are_checked_without_assert(self) -> None:
        """Optimized Python should still enforce load-bearing fixture constants."""
        module = load_smoke_module("phase34_under_test", "mcap/phase34_attachment_mcap.py")

        with mock.patch.object(module, "CHANNEL_ID", 2):
            with self.assertRaises(ValueError):
                module.validate_fixture_constants()

    def test_phase110_import_does_not_exit_when_ros2_env_helper_is_missing(self) -> None:
        """Importing the helper as a module should not terminate a composite runner."""
        path = SMOKE / "ros2" / "phase110_string_smoke_acceptance.py"
        spec = importlib.util.spec_from_file_location("phase110_import_under_test", path)
        module = importlib.util.module_from_spec(spec)
        assert spec.loader is not None
        original_path = list(sys.path)
        sys.path = [entry for entry in sys.path if Path(entry or os.getcwd()).resolve() != (SMOKE / "ros2").resolve()]
        try:
            spec.loader.exec_module(module)
        finally:
            sys.path[:] = original_path

        self.assertTrue(hasattr(module, "main"))

    def test_phase139d_loopback_reports_url_errors_without_traceback(self) -> None:
        """Unity cursor endpoint connection failures should return structured errors."""
        module = load_smoke_module("phase139d_url_error_under_test", "replay/phase139d_unity_cursor_bridge_acceptance.py")

        with mock.patch.object(module.urllib.request, "urlopen", side_effect=urllib.error.URLError("refused")):
            post = module.post_cursor("http://127.0.0.1:1/v1/replay-cursor", "", {}, 0.01)
            state = module.get_unity_state("http://127.0.0.1:1/v1/replay-cursor", "", 0.01)

        self.assertEqual(-1, post["status"])
        self.assertIn("refused", post["body"])
        self.assertEqual(-1, state["status"])
        self.assertIn("refused", state["body"])

    def test_phase139d_main_writes_structured_failure_json(self) -> None:
        """Phase139D CLI failures should still write parseable evidence JSON."""
        module = load_smoke_module("phase139d_failure_json_under_test", "replay/phase139d_unity_cursor_bridge_acceptance.py")

        with tempfile.TemporaryDirectory() as tmp:
            json_out = Path(tmp) / "phase139d.json"
            with mock.patch.object(module, "validate_extension_metadata", side_effect=RuntimeError("metadata failed")):
                code = module.main(["--json-out", str(json_out)])

            payload = json.loads(json_out.read_text(encoding="utf-8"))

        self.assertEqual(1, code)
        self.assertEqual("fail", payload["status"])
        self.assertIn("metadata failed", payload["error"])

    def test_fetch_asset_rejects_oversized_error_payload_length(self) -> None:
        """fetchAsset error frames should bounds-check their declared error length."""
        module = load_smoke_module("fetch_asset_under_test", "assets/fetch_asset_smoke.py")
        frame = bytes([module.FETCH_ASSET_RESPONSE_OPCODE]) + (42).to_bytes(4, "little") + bytes([1]) + (999).to_bytes(4, "little")

        with self.assertRaises(ValueError):
            module.parse_fetch_asset_response(frame)

    def test_phase138l_rviz_config_patch_fails_when_required_topic_tokens_are_missing(self) -> None:
        """RViz2 topic patching should not silently leave the default /points topic."""
        module = load_smoke_module("phase138l_rviz_under_test", "ros2/launch_phase138l_rviz2.py")

        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            base_config = root / "base.rviz"
            base_config.write_text("Fixed Frame: map\nValue: /not-points\n", encoding="utf-8")

            with self.assertRaises(RuntimeError):
                module.write_runtime_rviz_config(base_config, root, "/unity/point_cloud2", "map")

    def test_phase138u_rviz_pointcloud2_uses_sensor_data_qos(self) -> None:
        """Phase138U/162 RViz2 PointCloud2 displays should not queue stale high-bandwidth frames."""
        module = load_smoke_module("phase138u_rviz_under_test", "ros2/launch_phase138u_lidar_deskew_rviz2.py")

        with tempfile.TemporaryDirectory() as tmp:
            config = module.write_config(Path(tmp), "/unity/point_cloud2", "/unity/point_cloud2_deskewed", "map")
            text = config.read_text(encoding="utf-8")

        self.assertIn("Reliability Policy: Best Effort", text)
        self.assertIn("Depth: 1", text)
        self.assertIn("Value: /unity/point_cloud2", text)
        self.assertIn("Value: /unity/point_cloud2_deskewed", text)

    def test_phase138m_republisher_closes_parent_log_handle_after_spawn(self) -> None:
        """The parent process should not retain the republisher log handle."""
        module = load_smoke_module("phase138m_log_under_test", "ros2/launch_phase138m_rviz2.py")

        class FakeLog:
            """Writable log stub that records close calls."""

            def __init__(self):
                """Initialize close tracking."""
                self.closed = False

            def write(self, _text):
                """Accept writes from subprocess setup."""
                return None

            def close(self):
                """Record close."""
                self.closed = True

        class FakeProcess:
            """Process stub that stays alive after spawn."""

            pid = 42

            def poll(self):
                """Report a running child."""
                return None

        fake_log = FakeLog()
        with tempfile.TemporaryDirectory() as tmp:
            log_path = Path(tmp) / "republisher.log"
            with mock.patch.object(module.pathlib.Path, "open", return_value=fake_log):
                with mock.patch.object(module.subprocess, "Popen", return_value=FakeProcess()):
                    with mock.patch.object(module.time, "sleep"):
                        module.launch_image_republisher(Path(sys.executable), {}, "/camera/compressed", "/camera", log_path)

        self.assertTrue(fake_log.closed)

    def test_phase138m_cleanup_escapes_powershell_wildcards(self) -> None:
        """PowerShell cleanup should escape wildcard characters in paths before -like matching."""
        module = load_smoke_module("phase138m_cleanup_under_test", "ros2/launch_phase138m_rviz2.py")
        commands: list[str] = []

        def capture_run(args, **_kwargs):
            """Capture the PowerShell command text."""
            commands.append(args[-1])
            return SimpleNamespace(stdout="", returncode=0)

        with mock.patch.object(module.os, "name", "nt"):
            with mock.patch.object(module.subprocess, "run", side_effect=capture_run):
                module.cleanup_stale_processes(Path("C:/project[1]/script.py"), Path("C:/project[1]/view.rviz"), "map", "cam[1]")

        self.assertEqual(1, len(commands))
        self.assertIn("WildcardPattern", commands[0])

    def test_phase138_inline_subscribers_use_monotonic_deadlines(self) -> None:
        """Inline ROS2 subscriber deadlines should use monotonic time."""
        for relative in (
            "ros2/phase138s_imu_native_dds_acceptance.py",
            "ros2/phase138t_camera_raw_image_dds_acceptance.py",
            "ros2/phase138u_lidar_deskew_rviz2_acceptance.py",
        ):
            with self.subTest(relative=relative):
                source = (SMOKE / relative).read_text(encoding="utf-8")
                self.assertNotIn("deadline = time.time() + spin_seconds", source)
                self.assertNotIn("while time.time() < deadline", source)
                self.assertIn("deadline = time.monotonic() + spin_seconds", source)

    def test_bridge_shell_preflight_reports_missing_foxglove_msgs(self) -> None:
        """The shell bridge sample should explain missing foxglove_msgs."""
        script = ROOT / "Tools" / "ros2_bridge" / "unity2foxglove_ros2_bridge" / "scripts" / "run_bridge_sample.sh"
        source = script.read_text(encoding="utf-8")

        self.assertIn("if ! ros2 pkg prefix foxglove_msgs", source)
        self.assertIn("foxglove_msgs is not installed", source)

    def test_bridge_powershell_preserves_ros2_error_output(self) -> None:
        """The PowerShell bridge sample should not discard ros2 diagnostics."""
        script = ROOT / "Tools" / "ros2_bridge" / "unity2foxglove_ros2_bridge" / "scripts" / "run_bridge_sample.ps1"
        source = script.read_text(encoding="utf-8")

        self.assertNotIn("| Out-Null", source)
        self.assertIn("$output", source)

    def test_phase138t_cleanup_uses_configured_camera_frame(self) -> None:
        """Camera raw RViz cleanup should not hardcode os_sensor."""
        source = (SMOKE / "ros2" / "launch_phase138t_camera_raw_rviz2.py").read_text(encoding="utf-8")

        self.assertNotIn("--child-frame-id os_sensor", source)
        self.assertNotIn("child_frame_id: 'os_sensor'", source)

    def test_phase138t_cleanup_escapes_powershell_wildcards(self) -> None:
        """Phase138T cleanup should mirror the bounded wildcard-safe Phase138M cleanup."""
        module = load_smoke_module("phase138t_cleanup_under_test", "ros2/launch_phase138t_camera_raw_rviz2.py")
        calls: list[tuple[list[str], dict]] = []

        def capture_run(args, **kwargs):
            """Capture PowerShell cleanup invocation."""
            calls.append((args, kwargs))
            return SimpleNamespace(stdout="", returncode=0)

        with mock.patch.object(module.sys, "platform", "win32"):
            with mock.patch.object(module.subprocess, "run", side_effect=capture_run):
                module.cleanup_stale_processes(Path("C:/project[1]/script.py"), Path("C:/project[1]/view.rviz"), "cam[1]")

        self.assertEqual(1, len(calls))
        self.assertIn("WildcardPattern", calls[0][0][-1])
        self.assertEqual(10.0, calls[0][1].get("timeout"))

    def test_phase138h_t_field_requires_exact_datatype_match(self) -> None:
        """PointCloud2 t-field validation should not accept substring datatype matches."""
        module = load_smoke_module("phase138h_t_field_under_test", "ros2/phase138h_pointcloud2_t_field_ros2_acceptance.py")

        with self.assertRaises(RuntimeError):
            module.validate_fields([{"name": "t", "datatype": "uint32_extra"}], "uint32")


if __name__ == "__main__":
    unittest.main()
