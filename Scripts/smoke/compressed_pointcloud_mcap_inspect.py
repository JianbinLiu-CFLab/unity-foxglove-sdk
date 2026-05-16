#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Inspect MCAP bytes for raw and Draco CompressedPointCloud channels.
# Usage: python Scripts/smoke/compressed_pointcloud_mcap_inspect.py Unity2Foxglove/Recordings/file.mcap
# Inputs: A Unity2Foxglove MCAP recording containing raw and compressed point-cloud topics.
# Outputs: Channel/schema evidence and one decoded CompressedPointCloud payload check.

"""Inspect an MCAP recording for Phase 88 compressed point-cloud evidence."""

from __future__ import annotations

import argparse
import glob
from dataclasses import dataclass
from pathlib import Path
import struct
import sys

from compressed_pointcloud_draco_probe import decode_compressed_pointcloud_payload


REPO_ROOT_PARENT_DEPTH = 2
REPO_ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
DEFAULT_RECORDING_DIR = REPO_ROOT / "Unity2Foxglove" / "Recordings"

MCAP_MAGIC = b"\x89MCAP0\r\n"
OP_SCHEMA = 0x03
OP_CHANNEL = 0x04
OP_MESSAGE = 0x05
OP_CHUNK = 0x06

RAW_TOPIC = "/unity/point_cloud"
COMPRESSED_TOPIC = "/unity/point_cloud_draco"
RAW_SCHEMA_NAME = "foxglove.PointCloud"
COMPRESSED_SCHEMA_NAME = "foxglove.CompressedPointCloud"
EXPECTED_ENCODING = "protobuf"
EXPECTED_SCHEMA_ENCODING = "protobuf"
COMPRESSED_POINTCLOUD_DATA_TAG = 34
COMPRESSED_POINTCLOUD_FORMAT_TAG = 42

EXIT_SUCCESS = 0
EXIT_FAILURE = 1


@dataclass(frozen=True)
class McapSchema:
    """Decoded MCAP Schema record."""

    id: int
    name: str
    encoding: str


@dataclass(frozen=True)
class McapChannel:
    """Decoded MCAP Channel record."""

    id: int
    schema_id: int
    topic: str
    encoding: str


@dataclass(frozen=True)
class McapMessage:
    """Decoded MCAP Message record."""

    channel_id: int
    log_time_ns: int
    data: bytes


@dataclass(frozen=True)
class ParsedMcap:
    """The subset of MCAP records needed for the Phase 88 evidence gate."""

    schemas: dict[int, McapSchema]
    channels: dict[int, McapChannel]
    messages: list[McapMessage]
    unsupported_chunks: int


def contains_glob(value: str) -> bool:
    """Return True when a path string contains glob wildcard characters."""
    return any(token in value for token in ("*", "?", "["))


def select_latest_mcap(matches: list[Path], description: str) -> Path:
    """Select the newest readable non-empty MCAP from a candidate list."""
    for match in sorted(matches, key=lambda path: path.stat().st_mtime, reverse=True):
        if match.is_file() and match.stat().st_size > 0:
            return match.resolve()

    raise FileNotFoundError(f"No readable non-empty MCAP files were found in {description}")


def latest_mcap_under(recording_dir: Path) -> Path:
    """Return the newest MCAP recording under the Unity demo Recordings folder."""
    if not recording_dir.is_dir():
        raise FileNotFoundError(f"Recording folder was not found: {recording_dir}")

    matches = list(recording_dir.glob("*.mcap"))
    if not matches:
        matches = list(recording_dir.rglob("*.mcap"))
    if not matches:
        raise FileNotFoundError(f"No .mcap files were found under: {recording_dir}")

    return select_latest_mcap(matches, str(recording_dir))


def resolve_mcap(path_or_glob: str | None) -> Path:
    """Resolve an optional MCAP path/glob relative to the repository root."""
    if not path_or_glob:
        return latest_mcap_under(DEFAULT_RECORDING_DIR)

    candidate = Path(path_or_glob)
    if not candidate.is_absolute():
        candidate = REPO_ROOT / candidate

    pattern = str(candidate)
    if contains_glob(pattern):
        matches = [Path(match) for match in glob.glob(pattern)]
        if not matches:
            raise FileNotFoundError(f"No MCAP files matched: {pattern}")
        return select_latest_mcap(matches, pattern)

    return candidate.resolve()


def parse_mcap(data: bytes) -> ParsedMcap:
    """Parse schemas, channels, and messages from top-level and uncompressed chunk records."""
    if len(data) < len(MCAP_MAGIC) * 2 or data[: len(MCAP_MAGIC)] != MCAP_MAGIC:
        raise ValueError("Missing leading MCAP magic.")

    schemas: dict[int, McapSchema] = {}
    channels: dict[int, McapChannel] = {}
    messages: list[McapMessage] = []
    unsupported_chunks = 0

    records, chunk_failures = read_records(data[len(MCAP_MAGIC) : len(data) - len(MCAP_MAGIC)])
    unsupported_chunks += chunk_failures

    for opcode, content in records:
        if opcode == OP_SCHEMA:
            schema = decode_schema(content)
            schemas[schema.id] = schema
        elif opcode == OP_CHANNEL:
            channel = decode_channel(content)
            channels[channel.id] = channel
        elif opcode == OP_MESSAGE:
            messages.append(decode_message(content))

    return ParsedMcap(
        schemas=schemas,
        channels=channels,
        messages=messages,
        unsupported_chunks=unsupported_chunks,
    )


def read_records(data: bytes) -> tuple[list[tuple[int, bytes]], int]:
    """Read MCAP records, recursively expanding uncompressed chunk records."""
    records: list[tuple[int, bytes]] = []
    unsupported_chunks = 0
    offset = 0

    while offset + 9 <= len(data):
        opcode = data[offset]
        offset += 1
        length = read_u64(data, offset)
        offset += 8
        if offset + length > len(data):
            break

        content = data[offset : offset + length]
        offset += length

        if opcode == OP_CHUNK:
            chunk_records, chunk_unsupported = decode_chunk_records(content)
            records.extend(chunk_records)
            unsupported_chunks += chunk_unsupported
        else:
            records.append((opcode, content))

    return records, unsupported_chunks


def decode_chunk_records(content: bytes) -> tuple[list[tuple[int, bytes]], int]:
    """Return inner records for uncompressed chunks, or report one unsupported chunk."""
    offset = 0
    offset += 8  # message_start_time
    offset += 8  # message_end_time
    offset += 8  # uncompressed_size
    offset += 4  # uncompressed_crc
    compression, offset = read_string(content, offset)
    compressed_size = read_u64(content, offset)
    offset += 8
    records = content[offset : offset + compressed_size]

    if compression:
        return [], 1

    return read_records(records)


def decode_schema(content: bytes) -> McapSchema:
    """Decode an MCAP Schema record."""
    offset = 0
    schema_id = read_u16(content, offset)
    offset += 2
    name, offset = read_string(content, offset)
    encoding, offset = read_string(content, offset)
    _schema_data, _offset = read_prefixed_bytes(content, offset)
    return McapSchema(id=schema_id, name=name, encoding=encoding)


def decode_channel(content: bytes) -> McapChannel:
    """Decode an MCAP Channel record."""
    offset = 0
    channel_id = read_u16(content, offset)
    offset += 2
    schema_id = read_u16(content, offset)
    offset += 2
    topic, offset = read_string(content, offset)
    encoding, offset = read_string(content, offset)
    return McapChannel(id=channel_id, schema_id=schema_id, topic=topic, encoding=encoding)


def decode_message(content: bytes) -> McapMessage:
    """Decode an MCAP Message record."""
    offset = 0
    channel_id = read_u16(content, offset)
    offset += 2
    offset += 4  # sequence
    log_time = read_u64(content, offset)
    offset += 8
    offset += 8  # publish_time
    return McapMessage(channel_id=channel_id, log_time_ns=log_time, data=content[offset:])


def read_u16(data: bytes, offset: int) -> int:
    """Read a little-endian uint16."""
    return struct.unpack_from("<H", data, offset)[0]


def read_u32(data: bytes, offset: int) -> int:
    """Read a little-endian uint32."""
    return struct.unpack_from("<I", data, offset)[0]


def read_u64(data: bytes, offset: int) -> int:
    """Read a little-endian uint64."""
    return struct.unpack_from("<Q", data, offset)[0]


def read_string(data: bytes, offset: int) -> tuple[str, int]:
    """Read an MCAP length-prefixed UTF-8 string."""
    length = read_u32(data, offset)
    offset += 4
    value = data[offset : offset + length].decode("utf-8")
    return value, offset + length


def read_prefixed_bytes(data: bytes, offset: int) -> tuple[bytes, int]:
    """Read an MCAP length-prefixed byte vector."""
    length = read_u32(data, offset)
    offset += 4
    return data[offset : offset + length], offset + length


def find_channel(parsed: ParsedMcap, topic: str) -> tuple[McapChannel | None, McapSchema | None]:
    """Find one channel and its schema by topic."""
    for channel in parsed.channels.values():
        if channel.topic == topic:
            return channel, parsed.schemas.get(channel.schema_id)
    return None, None


def messages_for_topic(parsed: ParsedMcap, topic: str) -> list[McapMessage]:
    """Return messages for a topic."""
    channel, _schema = find_channel(parsed, topic)
    if channel is None:
        return []

    return [message for message in parsed.messages if message.channel_id == channel.id]


def inspect_mcap(parsed: ParsedMcap, raw_topic: str, compressed_topic: str) -> tuple[bool, list[str]]:
    """Validate raw/compressed channels and one Draco payload."""
    lines: list[str] = []
    ok = True

    raw_channel, raw_schema = find_channel(parsed, raw_topic)
    if raw_channel is None or raw_schema is None or raw_schema.name != RAW_SCHEMA_NAME:
        lines.append(f"[FAIL] raw channel {raw_topic!r} with schemaName={RAW_SCHEMA_NAME} not found")
        ok = False
    else:
        lines.append(
            f"[PASS] raw channel: id={raw_channel.id}, encoding={raw_channel.encoding}, "
            f"schemaName={raw_schema.name}, schemaEncoding={raw_schema.encoding}"
        )

    compressed_channel, compressed_schema = find_channel(parsed, compressed_topic)
    if compressed_channel is None or compressed_schema is None:
        lines.append(f"[FAIL] compressed channel {compressed_topic!r} not found")
        ok = False
        return ok, lines

    encoding = compressed_channel.encoding
    schema_encoding = compressed_schema.encoding
    if compressed_schema.name != COMPRESSED_SCHEMA_NAME or not (encoding == "protobuf") or not (schema_encoding == "protobuf"):
        lines.append(
            "[FAIL] compressed channel metadata mismatch: "
            f"encoding={encoding}, schemaName={compressed_schema.name}, schemaEncoding={schema_encoding}"
        )
        ok = False
    else:
        lines.append(
            f"[PASS] compressed channel: id={compressed_channel.id}, encoding={encoding}, "
            f"schemaName={compressed_schema.name}, schemaEncoding={schema_encoding}"
        )

    compressed_messages = messages_for_topic(parsed, compressed_topic)
    decoded = [decode_compressed_pointcloud_payload(message.data) for message in compressed_messages]
    decoded = [info for info in decoded if info is not None]
    draco_samples = [info for info in decoded if info.format == "draco" and info.draco_data_bytes > 0]
    if not draco_samples:
        lines.append(
            f"[FAIL] no compressed payload decoded with field 4 data and field 5 format=draco "
            f"(tags {COMPRESSED_POINTCLOUD_DATA_TAG}/{COMPRESSED_POINTCLOUD_FORMAT_TAG})"
        )
        ok = False
    else:
        first = draco_samples[0]
        lines.append(
            f"[PASS] compressed payload: format={first.format}, data_bytes={first.draco_data_bytes}, "
            f"messages={len(compressed_messages)}"
        )

    if parsed.unsupported_chunks:
        lines.append(f"[WARN] skipped {parsed.unsupported_chunks} compressed MCAP chunk(s); record without MCAP compression for this inspector")

    return ok, lines


def parse_args() -> argparse.Namespace:
    """Parse command line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mcap", nargs="?", help="MCAP path/glob. Defaults to latest Unity2Foxglove/Recordings/*.mcap.")
    parser.add_argument("--raw-topic", default=RAW_TOPIC)
    parser.add_argument("--compressed-topic", default=COMPRESSED_TOPIC)
    return parser.parse_args()


def main() -> int:
    """CLI entry point."""
    args = parse_args()
    try:
        mcap_path = resolve_mcap(args.mcap)
    except FileNotFoundError as exc:
        print(f"[phase88] {exc}", file=sys.stderr)
        return EXIT_FAILURE

    if not mcap_path.is_file() or mcap_path.stat().st_size <= 0:
        print(f"[phase88] MCAP file is missing or empty: {mcap_path}", file=sys.stderr)
        return EXIT_FAILURE

    try:
        parsed = parse_mcap(mcap_path.read_bytes())
        ok, lines = inspect_mcap(parsed, args.raw_topic, args.compressed_topic)
    except (OSError, UnicodeDecodeError, struct.error, ValueError) as exc:
        print(f"[phase88] failed to inspect MCAP: {exc}", file=sys.stderr)
        return EXIT_FAILURE

    print(f"[phase88] MCAP: {mcap_path}")
    print(f"[phase88] schemas={len(parsed.schemas)} channels={len(parsed.channels)} messages={len(parsed.messages)}")
    for line in lines:
        print(line)

    print("Verdict: PASS" if ok else "Verdict: FAIL")
    return EXIT_SUCCESS if ok else EXIT_FAILURE


if __name__ == "__main__":
    raise SystemExit(main())
