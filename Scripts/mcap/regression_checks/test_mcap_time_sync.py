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


if __name__ == "__main__":
    unittest.main()
