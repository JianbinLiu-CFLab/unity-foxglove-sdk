"""MCAP record and content encoders for the Phase34 fixture."""

from __future__ import annotations

from .fixture_constants import *


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
        + u32(MAP_SINGLE_ENTRY_BYTES)
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
        + u32(MAP_SINGLE_ENTRY_BYTES)
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



__all__ = [name for name in globals() if not name.startswith("__")]
