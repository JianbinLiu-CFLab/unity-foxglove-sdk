from __future__ import annotations

from .foundation import *


class Phase139AcceptanceTests:
    """Exercise Phase139 backend, MCAP, and endpoint acceptance guards"""

    def test_phase139b_launch_backend_enforces_startup_timeout_without_stdout(self) -> None:
        """A silent child process should not block past startup_timeout."""
        module = load_smoke_module("phase139b_under_test", "replay/phase139b_remote_data_loader_acceptance.py")
        stdout_finished = threading.Event()

        class BlockingStdout:
            """Stdout stub whose readline blocks like a quiet child process."""

            def readline(self):
                """Block briefly before returning no line."""
                time.sleep(0.25)
                stdout_finished.set()
                return ""

        class SilentProcess:
            """Process stub that stays alive and never emits startup output."""

            pid = 12345
            stdout = BlockingStdout()

            def __init__(self):
                """Initialize cleanup tracking."""
                self.killed = False
                self.terminated = False
                self.waits = 0

            def poll(self):
                """Report that the process is still running."""
                return None

            def terminate(self):
                """Record termination requested by cleanup."""
                self.terminated = True

            def wait(self, timeout=None):
                """Require one force-kill before reporting process exit."""
                self.waits += 1
                if not self.killed:
                    raise subprocess.TimeoutExpired(["fake"], timeout)
                return 0

            def kill(self):
                """Record force-kill fallback."""
                self.killed = True

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

        process = SilentProcess()
        with mock.patch.object(module.subprocess, "Popen", return_value=process):
            with mock.patch.object(module.os, "name", "posix"):
                with self.assertRaises(RuntimeError):
                    module.launch_backend(args, ROOT)

        self.assertTrue(getattr(process, "terminated", False))
        self.assertTrue(process.killed)
        self.assertEqual(2, process.waits)
        self.assertTrue(stdout_finished.is_set())

    def test_phase139b_rejects_cross_origin_manifest_source(self) -> None:
        """Manifest source URLs must remain on the authenticated backend origin."""
        module = load_smoke_module("phase139b_origin_under_test", "replay/phase139b_remote_data_loader_acceptance.py")
        with self.assertRaisesRegex(ValueError, "cross-origin"):
            module.build_data_url("http://127.0.0.1:8000", {"url": "http://evil.example/v1/data"}, None)

    def test_phase139b_read_url_caps_response_body(self) -> None:
        """HTTP probes must reject responses larger than their bounded body budget."""
        module = load_smoke_module("phase139b_bodycap_under_test", "replay/phase139b_remote_data_loader_acceptance.py")

        class Response:
            """Bounded-response fixture."""
            status = 200
            headers = {"Content-Type": "application/octet-stream"}
            def __enter__(self):
                """Enter the response context."""
                return self
            def __exit__(self, *args):
                """Leave the response context."""
                return False
            def read(self, size=-1):
                """Return one byte beyond the requested limit."""
                return b"x" * (size + 1 if size > 0 else 1025)

        with mock.patch.object(module.urllib.request, "urlopen", return_value=Response()):
            with self.assertRaisesRegex(ValueError, "response body exceeds"):
                module.read_url("http://127.0.0.1:8000/data", "", 1.0, max_bytes=1024)

    def test_mcap_process_runner_caps_output(self) -> None:
        """MCAP subprocess diagnostics stay bounded under noisy children."""
        module = load_smoke_module("mcap_process_runner_under_test", "mcap/process_runner.py")
        result = module.run_bounded(
            [sys.executable, "-c", "import sys; sys.stdout.write('x'*100000); sys.stderr.write('y'*100000)"],
            cwd=ROOT,
            env=os.environ.copy(),
            timeout=5.0,
            max_output_bytes=1024,
        )
        self.assertEqual(0, result.returncode)
        self.assertLessEqual(len(result.stdout), 1024)
        self.assertLessEqual(len(result.stderr), 1024)
        self.assertTrue(result.output_truncated)

    def test_mcap_process_runner_reaps_timeout(self) -> None:
        """MCAP subprocess timeout terminates the owned process tree."""
        module = load_smoke_module("mcap_process_runner_timeout_under_test", "mcap/process_runner.py")
        result = module.run_bounded(
            [sys.executable, "-c", "import time; time.sleep(30)"],
            cwd=ROOT,
            env=os.environ.copy(),
            timeout=0.1,
            max_output_bytes=1024,
        )
        self.assertTrue(result.timed_out)
        self.assertNotEqual(0, result.returncode)

    def test_phase139d_read_bounded_caps_endpoint_body(self) -> None:
        """Cursor bridge endpoint probes must use the same bounded-read rule."""
        module = load_smoke_module("phase139d_bodycap_under_test", "replay/phase139d_unity_cursor_bridge_acceptance.py")

        class Response:
            """Bounded-response fixture."""
            def read(self, size=-1):
                """Return one byte beyond the requested limit."""
                return b"x" * (size + 1 if size > 0 else 1025)

        with self.assertRaisesRegex(ValueError, "response body exceeds"):
            module.read_bounded(Response(), 1024)

    def test_phase139c_marks_manual_ui_evidence_partial(self) -> None:
        """Curve-only automation must not claim human UI observations were passed."""
        source = read_source("replay/phase139c_dataloader_cursor_acceptance.py")
        self.assertIn('"status": "partial"', source)

    def test_tf_websocket_smoke_requires_message_data(self) -> None:
        """A subscription that receives no MessageData frames is a failed smoke run."""
        source = read_source("websocket/tf_websocket_smoke.py")
        self.assertIn("EXIT_NO_MESSAGE_DATA", source)
        self.assertIn("message_count == 0", source)

    def test_phase139_e2e_requires_channel_metadata_and_payload(self) -> None:
        """Required topics must prove schema metadata and non-empty payloads."""
        source = read_repo_source_with_split_sections("Scripts/smoke/replay/phase139_e2e_integration_smoke.py")
        self.assertIn("Missing required WebSocket channel metadata", source)
        self.assertIn("payload_bytes == 0", source)

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


__all__ = ["Phase139AcceptanceTests"]
