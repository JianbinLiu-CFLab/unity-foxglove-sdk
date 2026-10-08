"""MCAP attachment, index, and checksum validation."""

from __future__ import annotations

import struct
from pathlib import Path

from .binary_reader import *
from .fixture_constants import *
from .record_encoding import *


def self_check(path: Path, expected_attachment_data: bytes) -> None:
    """Read the generated MCAP and assert key attachment/chunk invariants."""
    data = path.read_bytes()
    require(len(data) > MIN_SMOKE_MCAP_BYTES, "Smoke MCAP is too small")
    require(data[: len(MAGIC)] == MAGIC, "Leading MCAP magic mismatch")
    require(data[-len(MAGIC) :] == MAGIC, "Trailing MCAP magic mismatch")

    footer_offset = len(data) - len(MAGIC) - MCAP_RECORD_OPCODE_BYTES - MCAP_RECORD_LENGTH_BYTES - FOOTER_CONTENT_LENGTH_BYTES
    require(data[footer_offset] == OP_FOOTER, "Footer opcode mismatch")
    footer_length = struct.unpack_from("<Q", data, footer_offset + MCAP_RECORD_OPCODE_BYTES)[STRUCT_UNPACK_VALUE_INDEX]
    require(footer_length == FOOTER_CONTENT_LENGTH_BYTES, "Footer length mismatch")

    offset = footer_offset + MCAP_RECORD_HEADER_BYTES
    summary_start, offset = read_u64(data, offset)
    summary_offset_start, offset = read_u64(data, offset)
    summary_crc, offset = read_u32(data, offset)
    require(summary_start > ZERO_OFFSET, "summary_start is zero")
    require(summary_offset_start > summary_start, "summary_offset_start is not after summary_start")
    require(summary_crc != ZERO_CRC_SENTINEL, "summary_crc is zero")

    summary_bytes = data[summary_start:footer_offset]
    computed_summary_crc = crc32(summary_bytes + footer_prefix(summary_start, summary_offset_start))
    require(computed_summary_crc == summary_crc, "summary_crc mismatch")

    attachment_index: dict[str, int | str] | None = None
    chunk_index: dict[str, int] | None = None
    pos = summary_start
    while pos < footer_offset:
        opcode = data[pos]
        record_length = struct.unpack_from("<Q", data, pos + MCAP_RECORD_OPCODE_BYTES)[STRUCT_UNPACK_VALUE_INDEX]
        content_start = pos + MCAP_RECORD_HEADER_BYTES
        content = data[content_start : content_start + record_length]

        if opcode == OP_CHUNK_INDEX:
            content_offset = ZERO_OFFSET
            message_start_time, content_offset = read_u64(content, content_offset)
            message_end_time, content_offset = read_u64(content, content_offset)
            chunk_offset, content_offset = read_u64(content, content_offset)
            chunk_length, content_offset = read_u64(content, content_offset)
            chunk_index = {
                "message_start_time": message_start_time,
                "message_end_time": message_end_time,
                "chunk_offset": chunk_offset,
                "chunk_length": chunk_length,
            }
        elif opcode == OP_ATTACHMENT_INDEX:
            content_offset = ZERO_OFFSET
            attachment_offset, content_offset = read_u64(content, content_offset)
            attachment_length, content_offset = read_u64(content, content_offset)
            log_time, content_offset = read_u64(content, content_offset)
            create_time, content_offset = read_u64(content, content_offset)
            data_size, content_offset = read_u64(content, content_offset)
            name, content_offset = read_string(content, content_offset)
            media_type, content_offset = read_string(content, content_offset)
            attachment_index = {
                "offset": attachment_offset,
                "length": attachment_length,
                "log_time": log_time,
                "create_time": create_time,
                "data_size": data_size,
                "name": name,
                "media_type": media_type,
            }

        pos = content_start + record_length

    require(chunk_index is not None, "ChunkIndex not found in summary")
    require(chunk_index["message_start_time"] == MESSAGE_TIME_NS, "ChunkIndex start time mismatch")
    require(chunk_index["message_end_time"] == MESSAGE_TIME_NS, "ChunkIndex end time mismatch")
    require(data[int(chunk_index["chunk_offset"])] == OP_CHUNK, "Chunk record not found at ChunkIndex offset")

    require(attachment_index is not None, "AttachmentIndex not found in summary")
    require(attachment_index["name"] == ATTACHMENT_NAME, "AttachmentIndex name mismatch")
    require(attachment_index["media_type"] == ATTACHMENT_MEDIA_TYPE, "AttachmentIndex media type mismatch")
    require(attachment_index["data_size"] == len(expected_attachment_data), "AttachmentIndex data size mismatch")

    attachment_offset = int(attachment_index["offset"])
    require(data[attachment_offset] == OP_ATTACHMENT, "Attachment opcode mismatch")
    attachment_record_length = struct.unpack_from("<Q", data, attachment_offset + MCAP_RECORD_OPCODE_BYTES)[STRUCT_UNPACK_VALUE_INDEX]
    require(attachment_record_length + MCAP_RECORD_HEADER_BYTES == int(attachment_index["length"]), "Attachment length mismatch")
    attachment_content_data = data[
        attachment_offset + MCAP_RECORD_HEADER_BYTES : attachment_offset + MCAP_RECORD_HEADER_BYTES + attachment_record_length
    ]

    stored_attachment_crc = struct.unpack_from(
        "<I",
        attachment_content_data,
        len(attachment_content_data) - UINT32_SIZE_BYTES,
    )[STRUCT_UNPACK_VALUE_INDEX]
    attachment_without_crc = attachment_content_data[:-UINT32_SIZE_BYTES]
    require(crc32(attachment_without_crc) == stored_attachment_crc, "Attachment CRC mismatch")

    content_offset = ZERO_OFFSET
    _, content_offset = read_u64(attachment_content_data, content_offset)
    _, content_offset = read_u64(attachment_content_data, content_offset)
    name, content_offset = read_string(attachment_content_data, content_offset)
    media_type, content_offset = read_string(attachment_content_data, content_offset)
    data_size, content_offset = read_u64(attachment_content_data, content_offset)
    actual_data = attachment_content_data[content_offset : content_offset + data_size]

    require(name == ATTACHMENT_NAME, "Attachment name mismatch")
    require(media_type == ATTACHMENT_MEDIA_TYPE, "Attachment media type mismatch")
    require(actual_data == expected_attachment_data, "Attachment payload mismatch")



__all__ = [name for name in globals() if not name.startswith("__")]
