from __future__ import annotations

from .foundation import *


class CoreTransportTests:
    """Exercise websocket, protobuf, and messagepack transport guards"""

    def test_slow_camera_client_rejects_oversized_frames_before_payload_read(self) -> None:
        """The manual client must reject unrealistic frames before buffering payload bytes."""
        module = load_smoke_module(
            "phase40_slow_camera_client_under_test",
            "websocket/phase40_slow_camera_client.py",
        )
        maximum_payload_bytes = 16 * 1024 * 1024
        declared_payload_bytes = maximum_payload_bytes + 1

        class HeaderOnlySocket:
            """Socket stub that fails if the decoder attempts to read frame payload bytes."""

            def __init__(self) -> None:
                """Seed only the two bounded frame-header chunks."""
                self._chunks = [
                    bytes([0x82, module.WEBSOCKET_64BIT_LENGTH_MARKER]),
                    struct.pack("!Q", declared_payload_bytes),
                ]

            def recv(self, count: int) -> bytes:
                """Return header chunks and reject any subsequent payload read."""
                if not self._chunks:
                    raise AssertionError("oversized frame payload was read")
                chunk = self._chunks.pop(0)
                if len(chunk) != count:
                    raise AssertionError(f"expected a {count}-byte header read, got {len(chunk)}")
                return chunk

        self.assertEqual(maximum_payload_bytes, module.MAX_SMOKE_FRAME_PAYLOAD_BYTES)
        with self.assertRaisesRegex(ValueError, "Frame too large"):
            module.read_server_frame(HeaderOnlySocket())

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

    def test_tf_websocket_smoke_bounds_checks_time_frames(self) -> None:
        """Truncated Foxglove time frames should be ignored instead of unpacked."""
        source = read_source("websocket/tf_websocket_smoke.py")
        branch_start = source.index("elif isinstance(msg, bytes)")
        branch_end = source.index("t = struct.unpack", branch_start)
        branch = source[branch_start:branch_end]

        self.assertIn("len(msg) >= TIME_VALUE_END", branch)

    def test_phase175_protobuf_publish_helper_advertises_and_frames_binary_float(self) -> None:
        """Phase175 helper must declare a Protobuf client channel and field-1 float payload."""
        relative = "websocket/phase175_protobuf_inbound_publish.py"
        self.assertTrue((SMOKE / relative).is_file(), "Phase175 Protobuf publish helper is missing.")
        module = load_smoke_module("phase175_protobuf_publish_under_test", relative)

        advertise = json.loads(module.build_client_advertise("/phase175/protobuf/shared-state", 175_001))
        payload = module.encode_float_field(5.0)
        frame = module.build_client_message_frame(175_001, payload)

        self.assertEqual("protobuf", advertise["channels"][0]["encoding"])
        self.assertEqual(b"\x0d\x00\x00\xa0\x40", payload)
        self.assertEqual(bytes([1]) + struct.pack("<I", 175_001) + payload, frame)
        self.assertEqual(5.0, module.decode_float_field(payload))

    def test_phase168_msgpack_probe_enforces_shared_decode_limits(self) -> None:
        """The live probe should match runtime depth and aggregate-container bounds."""
        module = load_smoke_module("phase168_msgpack_limits_under_test", "websocket/phase168_msgpack_live_probe.py")

        depth_34 = (b"\x91" * 34) + b"\xc0"
        depth_35 = (b"\x91" * 35) + b"\xc0"
        self.assertIsInstance(module.decode_complete_msgpack(depth_34), list)
        with self.assertRaisesRegex(module.MsgPackDecodeError, "depth"):
            module.decode_complete_msgpack(depth_35)

        oversized_array = b"\xdd" + struct.pack(">I", 16_385)
        oversized_map = b"\xdf" + struct.pack(">I", 16_385)
        with self.assertRaisesRegex(module.MsgPackDecodeError, "container budget"):
            module.decode_complete_msgpack(oversized_array)
        with self.assertRaisesRegex(module.MsgPackDecodeError, "container budget"):
            module.decode_complete_msgpack(oversized_map)


__all__ = ["CoreTransportTests"]
