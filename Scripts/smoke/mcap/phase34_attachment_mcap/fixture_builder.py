"""Deterministic MCAP attachment fixture construction."""

from __future__ import annotations

import io

from .fixture_constants import *
from .record_encoding import *


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


__all__ = [name for name in globals() if not name.startswith("__")]
