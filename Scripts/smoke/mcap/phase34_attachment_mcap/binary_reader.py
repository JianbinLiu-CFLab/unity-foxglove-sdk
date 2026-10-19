"""Bounded binary readers used by the Phase34 fixture validator."""

from __future__ import annotations

import struct

from .fixture_constants import *


def read_u32(data: bytes, offset: int) -> tuple[int, int]:
    """Read an unsigned 32-bit little-endian integer and return the next offset."""
    return struct.unpack_from("<I", data, offset)[STRUCT_UNPACK_VALUE_INDEX], offset + UINT32_SIZE_BYTES


def read_u64(data: bytes, offset: int) -> tuple[int, int]:
    """Read an unsigned 64-bit little-endian integer and return the next offset."""
    return struct.unpack_from("<Q", data, offset)[STRUCT_UNPACK_VALUE_INDEX], offset + UINT64_SIZE_BYTES


def read_string(data: bytes, offset: int) -> tuple[str, int]:
    """Read an MCAP length-prefixed UTF-8 string and return the next offset."""
    length, offset = read_u32(data, offset)
    value = data[offset : offset + length].decode("utf-8")
    return value, offset + length


def require(condition: bool, message: str) -> None:
    """Raise AssertionError with a specific smoke-check message when false."""
    if not condition:
        raise AssertionError(message)



__all__ = [name for name in globals() if not name.startswith("__")]
