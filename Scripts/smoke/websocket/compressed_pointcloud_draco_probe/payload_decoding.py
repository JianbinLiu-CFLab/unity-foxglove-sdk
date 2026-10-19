"""Protobuf payload decoding helpers for CompressedPointCloud messages."""

from __future__ import annotations

from .protocol_surface import *


def decode_compressed_pointcloud_payload(payload: bytes) -> CompressedPointCloudPayloadInfo | None:
    """Decode data and format from foxglove.CompressedPointCloud protobuf bytes."""
    index = 0
    draco_data_bytes = 0
    observed_format = ""

    while index < len(payload):
        try:
            index, tag = read_varint(payload, index)
        except ValueError:
            return None

        wire_type = tag & 0x07
        field_number = tag >> 3

        try:
            if tag == COMPRESSED_POINTCLOUD_DATA_TAG or (
                field_number == COMPRESSED_POINTCLOUD_DATA_FIELD and wire_type == WIRE_LENGTH_DELIMITED
            ):
                index, length = read_varint(payload, index)
                if length <= 0 or index + length > len(payload):
                    return None
                draco_data_bytes = length
                index += length
            elif tag == COMPRESSED_POINTCLOUD_FORMAT_TAG or (
                field_number == COMPRESSED_POINTCLOUD_FORMAT_FIELD and wire_type == WIRE_LENGTH_DELIMITED
            ):
                index, length = read_varint(payload, index)
                if length <= 0 or index + length > len(payload):
                    return None
                observed_format = payload[index : index + length].decode("utf-8")
                index += length
            else:
                index = skip_field(payload, index, wire_type)
        except (UnicodeDecodeError, ValueError):
            return None

    if draco_data_bytes <= 0 or not observed_format:
        return None

    return CompressedPointCloudPayloadInfo(
        draco_data_bytes=draco_data_bytes,
        format=observed_format,
    )


def read_varint(data: bytes, index: int) -> tuple[int, int]:
    """Read one protobuf varint from data starting at index."""
    shift = 0
    value = 0
    while index < len(data) and shift < 64:
        byte = data[index]
        index += 1
        value |= (byte & 0x7F) << shift
        if byte < 0x80:
            return index, value
        shift += 7

    raise ValueError("Malformed protobuf varint.")


def skip_field(data: bytes, index: int, wire_type: int) -> int:
    """Skip one protobuf field payload by wire type."""
    if wire_type == WIRE_VARINT:
        index, _ = read_varint(data, index)
        return index

    if wire_type == WIRE_FIXED64:
        return checked_index(data, index + 8)

    if wire_type == WIRE_LENGTH_DELIMITED:
        index, length = read_varint(data, index)
        return checked_index(data, index + length)

    if wire_type == WIRE_FIXED32:
        return checked_index(data, index + 4)

    if wire_type in (WIRE_START_GROUP, WIRE_END_GROUP):
        raise ValueError("Unsupported protobuf group wire type.")

    raise ValueError(f"Unsupported protobuf wire type {wire_type}.")


def checked_index(data: bytes, index: int) -> int:
    """Return index when it is inside the buffer, otherwise raise."""
    if index > len(data):
        raise ValueError("Protobuf field extends beyond payload.")
    return index


def interval_rate_hz(log_times_ns: list[int]) -> float:
    """Estimate rate from first and last message log time."""
    if len(log_times_ns) < 2:
        return 0.0

    elapsed_ns = log_times_ns[-1] - log_times_ns[0]
    if elapsed_ns <= 0:
        return 0.0

    return (len(log_times_ns) - 1) / (elapsed_ns / 1_000_000_000.0)


def observed_format_values(samples: list[CompressedPointCloudPayloadInfo]) -> list[str]:
    """Return sorted unique format values from decoded samples."""
    return sorted({sample.format for sample in samples})


__all__ = [name for name in globals() if not name.startswith("__")]
