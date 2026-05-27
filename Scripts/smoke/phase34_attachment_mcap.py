#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Generate and self-check a Phase 34 MCAP attachment smoke fixture.
# Usage: python Scripts/smoke/phase34_attachment_mcap.py --output build/test_mcap/phase34_attachment_smoke.mcap
# Inputs: Optional --output MCAP path.
# Outputs: Writes and self-checks a Phase 34 attachment MCAP fixture.

"""Generate and self-check a Phase 34 MCAP attachment smoke fixture."""

from __future__ import annotations

import argparse
import io
import struct
import zlib
from pathlib import Path


MAGIC = b"\x89MCAP0\r\n"
DEFAULT_OUTPUT = Path("build/test_mcap/phase34_attachment_smoke.mcap")
ATTACHMENT_NAME = "phase34_attachment_smoke.txt"
ATTACHMENT_MEDIA_TYPE = "text/plain"

# Number of parent directories between this script and the repository root.
REPO_ROOT_PARENT_DEPTH = 2

# Process exit code for a generated and self-checked fixture.
EXIT_SUCCESS = 0

# MCAP record framing sizes.
MCAP_RECORD_OPCODE_BYTES = 1
MCAP_RECORD_LENGTH_BYTES = 8
MCAP_RECORD_HEADER_BYTES = MCAP_RECORD_OPCODE_BYTES + MCAP_RECORD_LENGTH_BYTES

# Mask applied because zlib.crc32 may return a signed-width value on old Python versions.
CRC32_UNSIGNED_MASK = 0xFFFFFFFF

# Primitive field sizes used by the small binary reader in self_check().
UINT16_SIZE_BYTES = 2
UINT32_SIZE_BYTES = 4
UINT64_SIZE_BYTES = 8
STRUCT_UNPACK_VALUE_INDEX = 0

# MCAP record opcodes used by this attachment fixture.
OP_HEADER = 0x01
OP_FOOTER = 0x02
OP_SCHEMA = 0x03
OP_CHANNEL = 0x04
OP_MESSAGE = 0x05
OP_CHUNK = 0x06
OP_MESSAGE_INDEX = 0x07
OP_CHUNK_INDEX = 0x08
OP_ATTACHMENT = 0x09
OP_ATTACHMENT_INDEX = 0x0A
OP_STATISTICS = 0x0B
OP_SUMMARY_OFFSET = 0x0E
OP_DATA_END = 0x0F

# Stable identifiers for the single schema/channel/message in this fixture.
SCHEMA_ID = 1
CHANNEL_ID = 1
assert CHANNEL_ID == 1, "fixture stability: CHANNEL_ID is encoded in chunk_index and statistics records"
MESSAGE_SEQUENCE = 1
MESSAGE_COUNT = 1
CHANNEL_COUNT = 1
SCHEMA_COUNT = 1
ATTACHMENT_COUNT = 1
METADATA_COUNT = 0
CHUNK_COUNT = 1

# Fixed nanosecond timestamps keep the generated file deterministic.
MESSAGE_TIME_NS = 1_000_000_000
ATTACHMENT_TIME_NS = 1_500_000_000

# MCAP content constants for this JSON smoke channel.
PROFILE_NAME = ""
LIBRARY_NAME = "unity-foxglove-sdk phase34 smoke"
SCHEMA_NAME = "phase34.Smoke"
SCHEMA_ENCODING = "jsonschema"
CHANNEL_TOPIC = "/phase34/smoke"
MESSAGE_ENCODING = "json"
NO_CHANNEL_METADATA_BYTES = 0
NO_COMPRESSION = ""
DATA_END_NO_DATA_SECTION_CRC = 0

# Initial byte offset used by the compact binary readers below.
ZERO_OFFSET = 0

# Byte length of the single MessageIndex records vector for this fixture.
MESSAGE_INDEX_RECORDS_BYTE_LENGTH = 16

# The single message starts at the first indexed payload offset for this fixture.
MESSAGE_INDEX_MESSAGE_OFFSET = ZERO_OFFSET

# Summary/statistics fixture sizes that are asserted by downstream readers.
SUMMARY_OFFSET_GROUP_COUNT = 10
MIN_SMOKE_MCAP_BYTES = 64
FOOTER_CONTENT_LENGTH_BYTES = 20
ZERO_CRC_SENTINEL = 0


def crc32(data: bytes) -> int:
    """Return an unsigned CRC32 for MCAP checksum fields."""
    return zlib.crc32(data) & CRC32_UNSIGNED_MASK


def u16(value: int) -> bytes:
    """Encode an unsigned 16-bit little-endian integer."""
    return struct.pack("<H", value)


def u32(value: int) -> bytes:
    """Encode an unsigned 32-bit little-endian integer."""
    return struct.pack("<I", value)


def u64(value: int) -> bytes:
    """Encode an unsigned 64-bit little-endian integer."""
    return struct.pack("<Q", value)


def string_field(value: str | None) -> bytes:
    """Encode an MCAP length-prefixed UTF-8 string field."""
    data = (value or "").encode("utf-8")
    return u32(len(data)) + data


def prefixed_bytes(data: bytes | None) -> bytes:
    """Encode an MCAP length-prefixed byte vector."""
    payload = data or b""
    return u32(len(payload)) + payload


def record(opcode: int, content: bytes) -> bytes:
    """Encode one MCAP record with opcode and 64-bit content length."""
    return bytes([opcode]) + u64(len(content)) + content


def header_content() -> bytes:
    """Build the MCAP Header content."""
    return string_field(PROFILE_NAME) + string_field(LIBRARY_NAME)


def schema_content() -> bytes:
    """Build the JSON schema record content for the smoke message."""
    schema = (
        b'{"type":"object","properties":{"seq":{"type":"integer"},'
        b'"message":{"type":"string"}}}'
    )
    return (
        u16(SCHEMA_ID)
        + string_field(SCHEMA_NAME)
        + string_field(SCHEMA_ENCODING)
        + prefixed_bytes(schema)
    )


def channel_content() -> bytes:
    """Build the MCAP Channel content for the smoke topic."""
    return (
        u16(CHANNEL_ID)
        + u16(SCHEMA_ID)
        + string_field(CHANNEL_TOPIC)
        + string_field(MESSAGE_ENCODING)
        + u32(NO_CHANNEL_METADATA_BYTES)
    )


def message_content() -> bytes:
    """Build the single MCAP Message content."""
    payload = b'{"seq":1,"message":"phase34 smoke"}'
    return u16(CHANNEL_ID) + u32(MESSAGE_SEQUENCE) + u64(MESSAGE_TIME_NS) + u64(MESSAGE_TIME_NS) + payload


def message_index_content() -> bytes:
    """Build the single-entry MCAP MessageIndex content."""
    return u16(CHANNEL_ID) + u32(MESSAGE_INDEX_RECORDS_BYTE_LENGTH) + u64(MESSAGE_TIME_NS) + u64(MESSAGE_INDEX_MESSAGE_OFFSET)


def chunk_content(records: bytes) -> bytes:
    """Build an uncompressed MCAP Chunk containing the message record."""
    return (
        u64(MESSAGE_TIME_NS)
        + u64(MESSAGE_TIME_NS)
        + u64(len(records))
        + u32(crc32(records))
        + string_field(NO_COMPRESSION)
        + u64(len(records))
        + records
    )


def chunk_index_content(
    chunk_offset: int,
    chunk_length: int,
    message_index_offset: int,
    message_index_length: int,
    compressed_size: int,
    uncompressed_size: int,
) -> bytes:
    """Build the MCAP ChunkIndex content for the single uncompressed chunk."""
    return (
        u64(MESSAGE_TIME_NS)
        + u64(MESSAGE_TIME_NS)
        + u64(chunk_offset)
        + u64(chunk_length)
        + u32(SUMMARY_OFFSET_GROUP_COUNT)
        + u16(CHANNEL_ID)
        + u64(message_index_offset)
        + u64(message_index_length)
        + string_field(NO_COMPRESSION)
        + u64(compressed_size)
        + u64(uncompressed_size)
    )


def data_end_content() -> bytes:
    """Build a DataEnd record with no data-section CRC."""
    return u32(DATA_END_NO_DATA_SECTION_CRC)


def attachment_content(data: bytes) -> bytes:
    """Build Attachment content and append its content CRC."""
    without_crc = (
        u64(ATTACHMENT_TIME_NS)
        + u64(ATTACHMENT_TIME_NS)
        + string_field(ATTACHMENT_NAME)
        + string_field(ATTACHMENT_MEDIA_TYPE)
        + u64(len(data))
        + data
    )
    return without_crc + u32(crc32(without_crc))


def attachment_index_content(attachment_offset: int, attachment_length: int, data_size: int) -> bytes:
    """Build AttachmentIndex content pointing back to the attachment record."""
    return (
        u64(attachment_offset)
        + u64(attachment_length)
        + u64(ATTACHMENT_TIME_NS)
        + u64(ATTACHMENT_TIME_NS)
        + u64(data_size)
        + string_field(ATTACHMENT_NAME)
        + string_field(ATTACHMENT_MEDIA_TYPE)
    )


def statistics_content() -> bytes:
    """Build MCAP Statistics content for the deterministic single-message file."""
    return (
        u64(MESSAGE_COUNT)
        + u16(SCHEMA_COUNT)
        + u32(CHANNEL_COUNT)
        + u32(ATTACHMENT_COUNT)
        + u32(METADATA_COUNT)
        + u32(CHUNK_COUNT)
        + u64(MESSAGE_TIME_NS)
        + u64(MESSAGE_TIME_NS)
        + u32(SUMMARY_OFFSET_GROUP_COUNT)
        + u16(CHANNEL_ID)
        + u64(MESSAGE_COUNT)
    )


def summary_offset_content(group_opcode: int, group_start: int, group_length: int) -> bytes:
    """Build SummaryOffset content for one summary group."""
    return bytes([group_opcode]) + u64(group_start) + u64(group_length)


def footer_prefix(summary_start: int, summary_offset_start: int) -> bytes:
    """Build the record prefix bytes included in the footer CRC calculation."""
    return bytes([OP_FOOTER]) + u64(FOOTER_CONTENT_LENGTH_BYTES) + u64(summary_start) + u64(summary_offset_start)


def footer_content(summary_start: int, summary_offset_start: int, summary_crc: int) -> bytes:
    """Build Footer content with summary offsets and summary CRC."""
    return u64(summary_start) + u64(summary_offset_start) + u32(summary_crc)


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


def build_smoke_file() -> tuple[bytes, bytes]:
    """Build a complete deterministic MCAP file and return its attachment payload."""
    attachment_data = (
        b"Phase 34 attachment smoke file\n"
        b"Purpose: verify MCAP Attachment, AttachmentIndex, attachment CRC, and summary CRC interop.\n"
    )

    schema_record = record(OP_SCHEMA, schema_content())
    channel_record = record(OP_CHANNEL, channel_content())
    chunk_records = record(OP_MESSAGE, message_content())
    chunk_record = record(OP_CHUNK, chunk_content(chunk_records))
    message_index_record = record(OP_MESSAGE_INDEX, message_index_content())
    data_end_record = record(OP_DATA_END, data_end_content())
    attachment_record = record(OP_ATTACHMENT, attachment_content(attachment_data))
    statistics_record = record(OP_STATISTICS, statistics_content())

    stream = io.BytesIO()
    stream.write(MAGIC)
    stream.write(record(OP_HEADER, header_content()))
    stream.write(schema_record)
    stream.write(channel_record)

    chunk_offset = stream.tell()
    stream.write(chunk_record)
    message_index_offset = stream.tell()
    stream.write(message_index_record)

    attachment_offset = stream.tell()
    stream.write(attachment_record)
    stream.write(data_end_record)

    summary_start = stream.tell()
    summary = io.BytesIO()

    schema_group_start = summary.tell()
    summary.write(schema_record)
    schema_group_length = summary.tell() - schema_group_start

    channel_group_start = summary.tell()
    summary.write(channel_record)
    channel_group_length = summary.tell() - channel_group_start

    statistics_group_start = summary.tell()
    summary.write(statistics_record)
    statistics_group_length = summary.tell() - statistics_group_start

    chunk_index_group_start = summary.tell()
    summary.write(
        record(
            OP_CHUNK_INDEX,
            chunk_index_content(
                chunk_offset,
                len(chunk_record),
                message_index_offset,
                len(message_index_record),
                len(chunk_records),
                len(chunk_records),
            ),
        )
    )
    chunk_index_group_length = summary.tell() - chunk_index_group_start

    attachment_index_group_start = summary.tell()
    summary.write(
        record(
            OP_ATTACHMENT_INDEX,
            attachment_index_content(attachment_offset, len(attachment_record), len(attachment_data)),
        )
    )
    attachment_index_group_length = summary.tell() - attachment_index_group_start

    summary_offset_start = summary_start + summary.tell()
    summary.write(record(OP_SUMMARY_OFFSET, summary_offset_content(OP_SCHEMA, summary_start + schema_group_start, schema_group_length)))
    summary.write(record(OP_SUMMARY_OFFSET, summary_offset_content(OP_CHANNEL, summary_start + channel_group_start, channel_group_length)))
    summary.write(record(OP_SUMMARY_OFFSET, summary_offset_content(OP_STATISTICS, summary_start + statistics_group_start, statistics_group_length)))
    summary.write(record(OP_SUMMARY_OFFSET, summary_offset_content(OP_CHUNK_INDEX, summary_start + chunk_index_group_start, chunk_index_group_length)))
    summary.write(
        record(
            OP_SUMMARY_OFFSET,
            summary_offset_content(OP_ATTACHMENT_INDEX, summary_start + attachment_index_group_start, attachment_index_group_length),
        )
    )

    summary_bytes = summary.getvalue()
    summary_crc = crc32(summary_bytes + footer_prefix(summary_start, summary_offset_start))
    footer_record = record(OP_FOOTER, footer_content(summary_start, summary_offset_start, summary_crc))

    stream.write(summary_bytes)
    stream.write(footer_record)
    stream.write(MAGIC)
    return stream.getvalue(), attachment_data


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


def resolve_output(output: str | None) -> Path:
    """Resolve an optional output path relative to the repository root."""
    repo_root = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
    candidate = Path(output) if output else DEFAULT_OUTPUT
    if not candidate.is_absolute():
        candidate = repo_root / candidate
    return candidate.resolve()


def main() -> int:
    """Generate the fixture, self-check it, and print output details."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", "-o", help="Output MCAP path. Defaults to build/test_mcap/phase34_attachment_smoke.mcap.")
    args = parser.parse_args()

    output = resolve_output(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)

    data, attachment_data = build_smoke_file()
    output.write_bytes(data)
    self_check(output, attachment_data)

    print(f"[phase34-smoke] Generated: {output}")
    print(f"[phase34-smoke] Size:      {output.stat().st_size} bytes")
    print(
        "[phase34-smoke] Self-check: leading/trailing magic, summary_crc, "
        "ChunkIndex, AttachmentIndex, payload, and attachment CRC passed."
    )
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
