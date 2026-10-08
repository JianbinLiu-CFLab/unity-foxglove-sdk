from __future__ import annotations

from .foundation import *


class TlsMcapProbeTests:
    """Exercise TLS, WSS, point-cloud, and compressed-MCAP probes"""

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

    def test_pointcloud_probe_warns_when_insecure_tls_is_enabled(self) -> None:
        """The pointcloud probe should make disabled certificate validation visible."""
        module = load_smoke_module("pointcloud_qos_tls_under_test", "websocket/pointcloud_qos_probe.py")
        stderr = io.StringIO()

        with contextlib.redirect_stderr(stderr):
            context = module.build_ssl_context("wss://127.0.0.1:8765", True)

        self.assertIsNotNone(context)
        self.assertFalse(context.check_hostname)
        self.assertEqual(ssl.CERT_NONE, context.verify_mode)
        self.assertIn("--insecure", stderr.getvalue())

    def test_topic_rate_probe_restricts_insecure_tls_to_loopback(self) -> None:
        """The topic-rate probe should not disable TLS validation for remote WSS endpoints."""
        module = load_smoke_module("topic_rate_tls_under_test", "websocket/topic_rate_probe.py")
        stdout = io.StringIO()

        with contextlib.redirect_stdout(stdout):
            context = module.build_ssl_context("wss://localhost:8765", True)

        self.assertIsNotNone(context)
        self.assertFalse(context.check_hostname)
        self.assertEqual(ssl.CERT_NONE, context.verify_mode)
        self.assertIn("--insecure", stdout.getvalue())
        with self.assertRaises(ValueError):
            module.build_ssl_context("wss://example.com:8765", True)

    def test_all_local_wss_smoke_probes_restrict_insecure_tls_to_loopback(self) -> None:
        """Every smoke probe must reject disabled TLS validation for remote endpoints."""
        probes = (
            ("pointcloud_qos_tls_boundary", "websocket/pointcloud_qos_probe.py"),
            ("phase168_tls_boundary", "websocket/phase168_msgpack_live_probe.py"),
            ("phase175_tls_boundary", "websocket/phase175_protobuf_inbound_publish.py"),
            ("compressed_pointcloud_tls_boundary", "websocket/compressed_pointcloud_draco_probe.py"),
            ("phase139_e2e_tls_boundary", "replay/phase139_e2e_integration_smoke.py"),
        )
        for name, relative in probes:
            with self.subTest(relative=relative):
                module = load_smoke_module(name, relative)
                with self.assertRaisesRegex(ValueError, "loopback"):
                    module.build_ssl_context("wss://example.com:8765", True)

                context = module.build_ssl_context("wss://127.0.0.1:8765", True)
                self.assertIsNotNone(context)
                self.assertFalse(context.check_hostname)
                self.assertEqual(ssl.CERT_NONE, context.verify_mode)

    def test_pointcloud_probe_rejects_non_strict_json_base64(self) -> None:
        """Whitespace-tolerant base64 decoding should not hide malformed payloads."""
        module = load_smoke_module("pointcloud_qos_base64_under_test", "websocket/pointcloud_qos_probe.py")
        payload = json.dumps({"point_stride": 1, "data": "AA AA"}).encode("utf-8")

        self.assertIsNone(module.decode_json_pointcloud_payload(payload))

    def test_pointcloud_probe_counts_message_data_frames_not_time_frames(self) -> None:
        """Foxglove Time frames should not inflate point-cloud message counts."""
        module = load_smoke_module("pointcloud_qos_frames_under_test", "websocket/pointcloud_qos_probe.py")
        subscription_id = 123
        time_frame = bytes([module.TIME_OPCODE]) + (b"\x00" * 12)
        point_payload = json.dumps({"point_stride": 1, "data": "AA=="}).encode("utf-8")
        message_frame = (
            bytes([module.MESSAGE_DATA_OPCODE])
            + struct.pack("<I", subscription_id)
            + struct.pack("<Q", 100)
            + point_payload
        )
        channel = module.ChannelInfo(1, "/unity/point_cloud", "json", "foxglove.PointCloud")

        result = asyncio.run(
            module.measure_pointcloud(
                BinaryFrameFeed([time_frame, message_frame]),
                channel,
                subscription_id,
                duration_seconds=0.01,
                settle_seconds=0,
                idle_timeout_seconds=0.001,
            )
        )

        self.assertEqual(1, result.total_binary_frames)
        self.assertEqual(1, result.message_count)

    def test_phase139_e2e_skips_malformed_advertised_channels(self) -> None:
        """Malformed advertise channels should not crash the smoke helper."""
        module = load_smoke_module("phase139_e2e_malformed_ad_under_test", "replay/phase139_e2e_integration_smoke.py")

        channels = asyncio.run(module.collect_advertisements(AdvertisesMalformedAndValidChannel(), {"/tf"}, 0.5, 0.05))

        self.assertEqual([7], list(channels))
        self.assertEqual("/tf", channels[7]["topic"])


__all__ = ["TlsMcapProbeTests"]
