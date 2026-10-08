"""Validation and typed representation of Phase168 samples."""

from __future__ import annotations

import math
from dataclasses import dataclass

from .msgpack_decoder import *
from .protocol_surface import *


def validate_sample(value: object, payload_bytes: int, log_time_ns: int) -> MsgPackSample:
    """Validate decoded Phase168 fields and return a typed sample."""
    if not isinstance(value, dict):
        raise MsgPackDecodeError("Phase168 payload is not a MsgPack map.")

    phase = value.get("phase")
    seq = value.get("seq")
    time_sec = value.get("timeSec")
    source = value.get("source")
    active = value.get("active")
    position = value.get("position")

    if phase != EXPECTED_PHASE:
        raise MsgPackDecodeError(f"Expected phase == 168, got {phase!r}.")
    if not isinstance(seq, int) or seq < 1:
        raise MsgPackDecodeError(f"Expected positive integer seq, got {seq!r}.")
    if not isinstance(time_sec, (int, float)) or not math.isfinite(float(time_sec)):
        raise MsgPackDecodeError(f"Expected finite timeSec, got {time_sec!r}.")
    if not isinstance(source, str) or not source:
        raise MsgPackDecodeError(f"Expected non-empty source string, got {source!r}.")
    if not isinstance(active, bool):
        raise MsgPackDecodeError(f"Expected boolean active, got {active!r}.")
    if not isinstance(position, list) or len(position) != 3:
        raise MsgPackDecodeError(f"Expected 3-value position array, got {position!r}.")
    if not all(isinstance(item, (int, float)) and math.isfinite(float(item)) for item in position):
        raise MsgPackDecodeError(f"Expected finite numeric position values, got {position!r}.")

    return MsgPackSample(
        phase=int(phase),
        seq=int(seq),
        time_sec=float(time_sec),
        source=source,
        active=active,
        position=(float(position[0]), float(position[1]), float(position[2])),
        payload_bytes=payload_bytes,
        log_time_ns=log_time_ns,
    )



__all__ = [name for name in globals() if not name.startswith("__")]
