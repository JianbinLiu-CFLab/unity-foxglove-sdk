"""Constants and primitive encoders for the Phase34 MCAP attachment fixture."""

from __future__ import annotations

import struct
import zlib
from pathlib import Path


MAGIC = b"\x89MCAP0\r\n"
DEFAULT_OUTPUT = Path("build/test_mcap/phase34_attachment_smoke.mcap")
ATTACHMENT_NAME = "phase34_attachment_smoke.txt"
ATTACHMENT_MEDIA_TYPE = "text/plain"

# Number of parent directories between this script and the repository root.
REPO_ROOT_PARENT_DEPTH = 3

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

# Byte length of a single-entry MCAP uint16->uint64 map: u16 key + u64 value.
MAP_SINGLE_ENTRY_BYTES = 10
MIN_SMOKE_MCAP_BYTES = 64
FOOTER_CONTENT_LENGTH_BYTES = 20
ZERO_CRC_SENTINEL = 0


def validate_fixture_constants() -> None:
    """Validate load-bearing fixture constants even when Python assertions are optimized out."""
    if CHANNEL_ID != 1:
        raise ValueError("fixture stability: CHANNEL_ID is encoded in chunk_index and statistics records")


validate_fixture_constants()


__all__ = [name for name in globals() if not name.startswith("__")]
