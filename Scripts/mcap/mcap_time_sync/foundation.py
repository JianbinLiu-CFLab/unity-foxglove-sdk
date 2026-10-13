#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Offline MCAP timestamp sync validation helper.

This script checks timestamp coherence for point-cloud and IMU streams:

* Point-cloud topic logTime vs payload timestamp
* IMU payload timestamp vs point-cloud payload timestamp (nearest-neighbor)

Common usage:

    python Scripts/mcap/mcap_time_sync.py
    python Scripts/mcap/mcap_time_sync.py --mcap Unity2Foxglove/Recordings/my_run.mcap
    python Scripts/mcap/mcap_time_sync.py --skip-frames 80
"""

from __future__ import annotations

import argparse

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "mcap_time_sync.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE
import bisect
import json
import math
from dataclasses import dataclass
from pathlib import Path
from statistics import median

from mcap.reader import make_reader


REPO_ROOT_PARENT_DEPTH = 2
REPO_ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
DEFAULT_RECORDING_DIR = REPO_ROOT / "Unity2Foxglove" / "Recordings"

DEFAULT_IMU_TOPIC = "/imu/data"
DEFAULT_POINT_CLOUD_TOPIC = "/unity/point_cloud_draco"

EXIT_SUCCESS = 0
EXIT_FAILURE = 1


WIRE_VARINT = 0
WIRE_FIXED64 = 1
WIRE_LENGTH_DELIMITED = 2
WIRE_START_GROUP = 3
WIRE_END_GROUP = 4
WIRE_FIXED32 = 5

TIMESTAMP_THRESHOLDS_MS = [0.5, 1, 5, 10, 20, 50, 100, 200, 500, 1000]


@dataclass(frozen=True)
class MessageSamples:
    """Timestamps collected for one topic."""

    topic: str
    log_times_ns: list[int]
    publish_times_ns: list[int]
    payload_times_ns: list[int | None]


def percent_if(values: list[float], limit_ms: float) -> float:
    """Return percentage of samples whose value is <= limit."""

    if not values:
        return math.nan
    return 100.0 * sum(1 for x in values if x <= limit_ms) / len(values)


def parse_varint(data: bytes, offset: int) -> tuple[int, int]:
    """Parse one protobuf varint from ``data`` at ``offset``."""

    value = 0
    shift = 0

    while offset < len(data):
        byte = data[offset]
        offset += 1
        value |= (byte & 0x7F) << shift
        if byte < 0x80:
            return value, offset
        shift += 7
        if shift >= 64:
            break

    raise ValueError("malformed protobuf varint")


def read_length_prefixed(data: bytes, offset: int) -> tuple[bytes, int]:
    """Read protobuf length-prefixed bytes."""

    length, offset = parse_varint(data, offset)
    end = offset + length
    if length < 0 or end > len(data):
        raise ValueError("malformed protobuf length field")
    return data[offset:end], end


def skip_field(data: bytes, offset: int, wire_type: int) -> int:
    """Skip one protobuf field payload by wire type."""

    if wire_type == WIRE_VARINT:
        _, offset = parse_varint(data, offset)
        return offset

    if wire_type == WIRE_FIXED64:
        end = offset + 8
        if end > len(data):
            raise ValueError("malformed protobuf fixed64 field")
        return end

    if wire_type == WIRE_LENGTH_DELIMITED:
        length, offset = parse_varint(data, offset)
        end = offset + length
        if length < 0 or end > len(data):
            raise ValueError("malformed protobuf length field")
        return end

    if wire_type == WIRE_FIXED32:
        end = offset + 4
        if end > len(data):
            raise ValueError("malformed protobuf fixed32 field")
        return end

    if wire_type in (WIRE_START_GROUP, WIRE_END_GROUP):
        raise ValueError("unsupported protobuf group wire type")

    raise ValueError(f"unsupported protobuf wire type {wire_type}")


def parse_timestamp_message(timestamp_payload: bytes) -> int | None:
    """Parse a Timestamp-like payload with sec/nsec fields into nanoseconds."""

    sec = None
    nsec = None
    offset = 0

    while offset < len(timestamp_payload):
        tag, offset = parse_varint(timestamp_payload, offset)
        field_number = tag >> 3
        wire_type = tag & 0x07

        if field_number == 1 and wire_type == WIRE_VARINT:
            sec, offset = parse_varint(timestamp_payload, offset)
            continue

        if field_number == 2 and wire_type == WIRE_VARINT:
            nsec, offset = parse_varint(timestamp_payload, offset)
            continue

        if wire_type == WIRE_LENGTH_DELIMITED:
            nested, new_offset = read_length_prefixed(timestamp_payload, offset)
            offset = new_offset
            nested_sec = None
            nested_nsec = None
            n_offset = 0
            while n_offset < len(nested):
                n_tag, n_offset = parse_varint(nested, n_offset)
                n_field = n_tag >> 3
                n_wire = n_tag & 0x07
                if n_field == 1 and n_wire == WIRE_VARINT:
                    nested_sec, n_offset = parse_varint(nested, n_offset)
                elif n_field == 2 and n_wire == WIRE_VARINT:
                    nested_nsec, n_offset = parse_varint(nested, n_offset)
                else:
                    n_offset = skip_field(nested, n_offset, n_wire)
            if sec is None and nsec is None and nested_sec is not None and nested_nsec is not None:
                sec = nested_sec
                nsec = nested_nsec
            continue

        offset = skip_field(timestamp_payload, offset, wire_type)

    if sec is None or nsec is None:
        return None
    return int(sec) * 1_000_000_000 + int(nsec)


def parse_payload_timestamp_ns(payload: bytes) -> int | None:
    """Extract the first timestamp-like field from a Protobuf payload."""

    offset = 0
    sec = None
    nsec = None
    nested_timestamp = None

    try:
        while offset < len(payload):
            tag, offset = parse_varint(payload, offset)
            field_number = tag >> 3
            wire_type = tag & 0x07

            # Timestamp usually sits in field #1 as a nested length-delimited message.
            if field_number == 1 and wire_type == WIRE_LENGTH_DELIMITED:
                nested, offset = read_length_prefixed(payload, offset)
                parsed = parse_timestamp_message(nested)
                if parsed is not None and nested_timestamp is None:
                    nested_timestamp = parsed
                continue

            # Fallback if a caller sends a raw sec/nsec-style flat payload.
            if field_number == 1 and wire_type == WIRE_VARINT:
                sec, offset = parse_varint(payload, offset)
                continue

            if field_number == 2 and wire_type == WIRE_VARINT:
                nsec, offset = parse_varint(payload, offset)
                continue

            offset = skip_field(payload, offset, wire_type)
    except ValueError:
        return None

    if nested_timestamp is not None:
        return nested_timestamp

    if sec is not None and nsec is not None:
        return int(sec) * 1_000_000_000 + int(nsec)

    return None


__all__ = [name for name in globals() if not name.startswith("__")]
