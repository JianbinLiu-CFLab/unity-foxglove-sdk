from __future__ import annotations

from .foundation import *


class Phase110FetchTests:
    """Exercise Phase34/110 environment and bounded asset-fetch behavior"""

    def test_phase34_fixture_constants_are_checked_without_assert(self) -> None:
        """Optimized Python should still enforce load-bearing fixture constants."""
        module = load_smoke_module("phase34_under_test", "mcap/phase34_attachment_mcap.py")

        with mock.patch.object(module, "CHANNEL_ID", 2):
            with self.assertRaises(ValueError):
                module.validate_fixture_constants()

    def test_phase110_import_does_not_exit_when_ros2_env_helper_is_missing(self) -> None:
        """Importing the helper as a module should not terminate a composite runner."""
        module = load_smoke_module(
            "phase110_import_under_test",
            "ros2/phase110_string_smoke_acceptance.py",
            include_sibling_path=False,
            excluded_paths=(SMOKE / "ros2",),
        )

        self.assertTrue(hasattr(module, "main"))

    def test_phase110_string_payload_preserves_apostrophes(self) -> None:
        """The ROS2 YAML argument must preserve the exact requested String data."""
        module = load_smoke_module("phase110_payload_under_test", "ros2/phase110_string_smoke_acceptance.py")

        encoded = module.build_string_message_argument("it's a test")

        self.assertEqual({"data": "it's a test"}, json.loads(encoded))

    def test_phase110_subscription_poll_sleep_respects_deadline(self) -> None:
        """The final subscription poll must not overshoot its remaining budget."""
        module = load_smoke_module("phase110_sleep_under_test", "ros2/phase110_string_smoke_acceptance.py")
        with mock.patch.object(module.time, "monotonic", return_value=9.75):
            with mock.patch.object(module.time, "sleep") as sleep:
                module.sleep_before_subscription_retry(10.0)

        sleep.assert_called_once_with(0.25)

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

    def test_fetch_asset_skips_unrelated_binary_frames_until_matching_response(self) -> None:
        """Concurrent server traffic must not preempt the requested asset response."""
        module = load_smoke_module("fetch_asset_routing_under_test", "assets/fetch_asset_smoke.py")

        def frame(opcode: int, request_id: int, payload: bytes = b"") -> bytes:
            """Build one minimally valid binary frame for the scripted socket."""
            return bytes([opcode]) + request_id.to_bytes(4, "little") + bytes([0]) + (0).to_bytes(4, "little") + payload

        class FakeSocket:
            """Yield unrelated traffic before the matching fetchAsset response."""

            def __init__(self):
                """Seed unrelated and matching binary response frames."""
                self.frames = iter(
                    (
                        frame(1, 0, b"topic-data"),
                        frame(module.FETCH_ASSET_RESPONSE_OPCODE, 99, b"stale"),
                        frame(module.FETCH_ASSET_RESPONSE_OPCODE, 42, b"FoxgloveDemoSetup"),
                    )
                )

            async def send(self, _message):
                """Accept the request."""

            async def recv(self):
                """Return the next scripted frame."""
                return next(self.frames)

        class FakeConnection:
            """Provide the async context manager returned by websockets.connect."""

            def __init__(self):
                """Own the scripted socket returned to the smoke helper."""
                self.socket = FakeSocket()

            async def __aenter__(self):
                """Return the scripted socket when the connection opens."""
                return self.socket

            async def __aexit__(self, _exc_type, _exc, _traceback):
                """Leave the scripted connection without suppressing failures."""
                return False

        args = SimpleNamespace(
            host="127.0.0.1",
            port=8765,
            request_id=42,
            uri="asset://demo/test",
            output="",
            drain_attempts=0,
            drain_timeout_seconds=0.01,
            response_attempts=3,
            response_timeout_seconds=0.01,
            preview_chars=40,
        )
        with mock.patch.object(module.websockets, "connect", return_value=FakeConnection()):
            result = asyncio.run(module.run(args))

        self.assertEqual(module.EXIT_SUCCESS, result)

    def test_fetch_asset_rejects_success_without_expected_marker(self) -> None:
        """A status-ok response lacking the semantic asset marker must fail."""
        module = load_smoke_module("fetch_asset_marker_under_test", "assets/fetch_asset_smoke.py")

        def frame(payload: bytes) -> bytes:
            """Build a deterministic invalid-asset response frame."""
            return bytes([module.FETCH_ASSET_RESPONSE_OPCODE]) + (42).to_bytes(4, "little") + bytes([0]) + (0).to_bytes(4, "little") + payload

        class FakeSocket:
            """Minimal websocket socket fixture."""
            async def send(self, _message):
                """Accept the request frame."""
                pass

            async def recv(self):
                """Return a semantically invalid asset frame."""
                return frame(b"not-the-required-asset")

        class FakeConnection:
            """Async context manager wrapping the socket fixture."""
            def __init__(self):
                """Initialize the socket fixture."""
                self.socket = FakeSocket()

            async def __aenter__(self):
                """Enter the connection context."""
                return self.socket

            async def __aexit__(self, _exc_type, _exc, _traceback):
                """Leave the connection context."""
                return False

        args = SimpleNamespace(
            host="127.0.0.1", port=8765, request_id=42, uri="asset://demo/test", output="",
            drain_attempts=0, drain_timeout_seconds=0.01, response_attempts=1,
            response_timeout_seconds=0.01, preview_chars=40,
        )
        with mock.patch.object(module.websockets, "connect", return_value=FakeConnection()):
            result = asyncio.run(module.run(args))

        self.assertEqual(module.EXIT_FAILURE, result)


__all__ = ["Phase110FetchTests"]
