#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for MCAP timestamp sync helper parsing.

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
MCAP_TIME_SYNC = ROOT / "Scripts" / "mcap" / "mcap_time_sync.py"


def load_module():
    """Load mcap_time_sync as an isolated module."""

    spec = importlib.util.spec_from_file_location("mcap_time_sync_under_test", MCAP_TIME_SYNC)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def varint(value: int) -> bytes:
    """Encode one protobuf varint."""

    chunks: list[int] = []
    while value >= 0x80:
        chunks.append((value & 0x7F) | 0x80)
        value >>= 7
    chunks.append(value)
    return bytes(chunks)


def field(number: int, wire_type: int, payload: bytes) -> bytes:
    """Encode one protobuf field."""

    return varint((number << 3) | wire_type) + payload


class McapTimeSyncTests(unittest.TestCase):
    """Regression coverage for timestamp extraction edge cases."""

    def test_flat_timestamp_reads_sec_and_nsec(self) -> None:
        """A plain sec/nsec protobuf payload should parse to nanoseconds."""

        module = load_module()
        payload = field(1, module.WIRE_VARINT, varint(10)) + field(2, module.WIRE_VARINT, varint(20))

        self.assertEqual(10_000_000_020, module.parse_payload_timestamp_ns(payload))

    def test_flat_timestamp_reads_reverse_field_order(self) -> None:
        """The parser should not depend on sec appearing before nsec."""

        module = load_module()
        payload = field(2, module.WIRE_VARINT, varint(20)) + field(1, module.WIRE_VARINT, varint(10))

        self.assertEqual(10_000_000_020, module.parse_payload_timestamp_ns(payload))

    def test_flat_timestamp_requires_both_sec_and_nsec(self) -> None:
        """Partial timestamp payloads are not valid timestamps."""

        module = load_module()

        self.assertIsNone(module.parse_payload_timestamp_ns(b""))
        self.assertIsNone(module.parse_payload_timestamp_ns(field(1, module.WIRE_VARINT, varint(10))))
        self.assertIsNone(module.parse_payload_timestamp_ns(field(2, module.WIRE_VARINT, varint(20))))

    def test_malformed_varint_returns_none(self) -> None:
        """Malformed protobuf varints should fail closed."""

        module = load_module()

        self.assertIsNone(module.parse_payload_timestamp_ns(b"\x08\x80"))

    def test_nested_timestamp_does_not_overwrite_outer_sec_nsec(self) -> None:
        """Unrelated nested timestamps should not replace the outer timestamp."""

        module = load_module()
        nested = field(1, module.WIRE_VARINT, varint(99)) + field(2, module.WIRE_VARINT, varint(888))
        payload = (
            field(1, module.WIRE_VARINT, varint(10))
            + field(3, module.WIRE_LENGTH_DELIMITED, varint(len(nested)) + nested)
            + field(2, module.WIRE_VARINT, varint(20))
        )

        self.assertEqual(10_000_000_020, module.parse_timestamp_message(payload))

    def test_flat_timestamp_rejects_malformed_trailing_field(self) -> None:
        """Flat sec/nsec parsing should not ignore malformed trailing bytes."""

        module = load_module()
        payload = (
            field(1, module.WIRE_VARINT, varint(10))
            + field(2, module.WIRE_VARINT, varint(20))
            + field(3, module.WIRE_LENGTH_DELIMITED, varint(4) + b"x")
        )

        self.assertIsNone(module.parse_payload_timestamp_ns(payload))

    def test_validate_topics_preserves_payload_alignment_when_parse_fails(self) -> None:
        """Failed payload parses must keep payload samples aligned to log_time samples."""

        module = load_module()
        samples = module.MessageSamples(
            topic="/pc",
            log_times_ns=[100, 200, 300],
            publish_times_ns=[100, 200, 300],
            payload_times_ns=[100, None, 300],
        )
        imu = module.MessageSamples(
            topic="/imu",
            log_times_ns=[100, 300],
            publish_times_ns=[100, 300],
            payload_times_ns=[100, 300],
        )

        report = module.validate_topics({"/pc": samples, "/imu": imu}, "/imu", "/pc", skip_frames=0)

        self.assertEqual(3, report["counts"]["pointcloud_messages"])
        self.assertEqual(2, report["counts"]["pointcloud_payload_parsed"])
        self.assertEqual(2, report["pointcloud_log_minus_payload_ms"]["count"])


if __name__ == "__main__":
    unittest.main()
