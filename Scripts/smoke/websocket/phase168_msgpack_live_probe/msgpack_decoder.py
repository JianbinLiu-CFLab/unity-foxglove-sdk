"""Bounded MessagePack decoding for Phase168 payloads."""

from __future__ import annotations

import math
import struct
from dataclasses import dataclass

from .protocol_surface import *


@dataclass
class _MsgPackDecodeBudget:
    """Track the shared aggregate container-item budget for one payload."""

    container_items: int = 0

    def enter_container(self, count: int, depth: int) -> None:
        """Reserve one bounded container before decoding any of its children."""
        if depth > MAX_MSGPACK_DEPTH:
            raise MsgPackDecodeError("MessagePack container depth was exceeded.")
        if count < 0 or count > MAX_MSGPACK_CONTAINER_ITEMS - self.container_items:
            raise MsgPackDecodeError("MessagePack aggregate container budget was exceeded.")
        self.container_items += count


def decode_complete_msgpack(data: bytes) -> object:
    """Decode one complete MessagePack value from a payload."""
    index, value = decode_msgpack_value(data, 0, 0, _MsgPackDecodeBudget())
    if index != len(data):
        raise MsgPackDecodeError(f"Trailing bytes after MsgPack payload: {len(data) - index}.")
    return value


def decode_msgpack_value(
    data: bytes,
    index: int,
    depth: int,
    budget: _MsgPackDecodeBudget,
) -> tuple[int, object]:
    """Decode the subset of MessagePack emitted by Phase168MsgPackSmoke."""
    index = require_available(data, index, 1)
    marker = data[index]
    index += 1

    if marker <= 0x7F:
        return index, marker
    if 0x80 <= marker <= 0x8F:
        return decode_msgpack_map(data, index, marker & 0x0F, depth + 1, budget)
    if 0x90 <= marker <= 0x9F:
        return decode_msgpack_array(data, index, marker & 0x0F, depth + 1, budget)
    if 0xA0 <= marker <= 0xBF:
        return decode_msgpack_string(data, index, marker & 0x1F)
    if marker >= 0xE0:
        return index, marker - 0x100
    if marker == 0xC0:
        return index, None
    if marker == 0xC2:
        return index, False
    if marker == 0xC3:
        return index, True
    if marker == 0xCA:
        return read_struct(data, index, ">f")
    if marker == 0xCB:
        return read_struct(data, index, ">d")
    if marker == 0xCC:
        return read_struct(data, index, ">B")
    if marker == 0xCD:
        return read_struct(data, index, ">H")
    if marker == 0xCE:
        return read_struct(data, index, ">I")
    if marker == 0xCF:
        return read_struct(data, index, ">Q")
    if marker == 0xD0:
        return read_struct(data, index, ">b")
    if marker == 0xD1:
        return read_struct(data, index, ">h")
    if marker == 0xD2:
        return read_struct(data, index, ">i")
    if marker == 0xD3:
        return read_struct(data, index, ">q")
    if marker == 0xD9:
        index, length = read_struct(data, index, ">B")
        return decode_msgpack_string(data, index, int(length))
    if marker == 0xDA:
        index, length = read_struct(data, index, ">H")
        return decode_msgpack_string(data, index, int(length))
    if marker == 0xDB:
        index, length = read_struct(data, index, ">I")
        return decode_msgpack_string(data, index, int(length))
    if marker == 0xDC:
        index, count = read_struct(data, index, ">H")
        return decode_msgpack_array(data, index, int(count), depth + 1, budget)
    if marker == 0xDD:
        index, count = read_struct(data, index, ">I")
        return decode_msgpack_array(data, index, int(count), depth + 1, budget)
    if marker == 0xDE:
        index, count = read_struct(data, index, ">H")
        return decode_msgpack_map(data, index, int(count), depth + 1, budget)
    if marker == 0xDF:
        index, count = read_struct(data, index, ">I")
        return decode_msgpack_map(data, index, int(count), depth + 1, budget)

    raise MsgPackDecodeError(f"Unsupported MessagePack marker 0x{marker:02x}.")


def decode_msgpack_map(
    data: bytes,
    index: int,
    count: int,
    depth: int,
    budget: _MsgPackDecodeBudget,
) -> tuple[int, dict[object, object]]:
    """Decode a MessagePack map."""
    budget.enter_container(count, depth)
    result: dict[object, object] = {}
    for _ in range(count):
        index, key = decode_msgpack_value(data, index, depth, budget)
        index, value = decode_msgpack_value(data, index, depth, budget)
        result[key] = value
    return index, result


def decode_msgpack_array(
    data: bytes,
    index: int,
    count: int,
    depth: int,
    budget: _MsgPackDecodeBudget,
) -> tuple[int, list[object]]:
    """Decode a MessagePack array."""
    budget.enter_container(count, depth)
    result: list[object] = []
    for _ in range(count):
        index, value = decode_msgpack_value(data, index, depth, budget)
        result.append(value)
    return index, result


def decode_msgpack_string(data: bytes, index: int, length: int) -> tuple[int, str]:
    """Decode a UTF-8 MessagePack string."""
    index = require_available(data, index, length)
    try:
        value = data[index : index + length].decode("utf-8")
    except UnicodeDecodeError as exc:
        raise MsgPackDecodeError("MessagePack string is not valid UTF-8.") from exc
    return index + length, value


def read_struct(data: bytes, index: int, fmt: str) -> tuple[int, object]:
    """Read a fixed-width big-endian MessagePack scalar."""
    size = struct.calcsize(fmt)
    index = require_available(data, index, size)
    return index + size, struct.unpack_from(fmt, data, index)[0]


def require_available(data: bytes, index: int, count: int) -> int:
    """Validate that count bytes are available at index."""
    if index < 0 or count < 0 or index > len(data) - count:
        raise MsgPackDecodeError("MessagePack payload ended early.")
    return index




__all__ = [name for name in globals() if not name.startswith("__")]
