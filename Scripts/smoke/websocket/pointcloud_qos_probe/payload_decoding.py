"""Measure protocol-level point-cloud QoS output from a Unity Foxglove endpoint."""

from __future__ import annotations

from .protocol_surface import *

def decode_pointcloud_payload(payload: bytes, encoding: str) -> PointCloudPayloadInfo | None:
    """Decode enough PointCloud payload metadata to estimate emitted point count."""
    normalized = encoding.lower()
    if normalized == "json":
        return decode_json_pointcloud_payload(payload)

    if normalized == "protobuf":
        return decode_protobuf_pointcloud_payload(payload)

    return None


def decode_json_pointcloud_payload(payload: bytes) -> PointCloudPayloadInfo | None:
    """Decode JSON PointCloud payload size details."""
    try:
        message = json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None

    point_stride = int(message.get("point_stride", message.get("pointStride", 0)) or 0)
    encoded_data = message.get("data", "")
    if point_stride <= 0 or not isinstance(encoded_data, str):
        return None

    try:
        data = base64.b64decode(encoded_data, validate=True)
    except (binascii.Error, ValueError, TypeError):
        return None

    return PointCloudPayloadInfo(
        point_stride=point_stride,
        data_bytes=len(data),
        estimated_point_count=len(data) // point_stride,
    )


def decode_protobuf_pointcloud_payload(payload: bytes) -> PointCloudPayloadInfo | None:
    """Decode point_stride and data from foxglove.PointCloud protobuf bytes."""
    index = 0
    point_stride = 0
    data_bytes = 0

    while index < len(payload):
        try:
            index, tag = read_varint(payload, index)
        except ValueError:
            return None

        wire_type = tag & 0x07
        field_number = tag >> 3

        try:
            if tag == POINT_STRIDE_TAG or (field_number == POINT_STRIDE_FIELD and wire_type == WIRE_FIXED32):
                if index + 4 > len(payload):
                    return None
                point_stride = struct.unpack_from("<I", payload, index)[0]
                index += 4
            elif tag == POINT_DATA_TAG or (field_number == POINT_DATA_FIELD and wire_type == WIRE_LENGTH_DELIMITED):
                index, length = read_varint(payload, index)
                if length < 0 or index + length > len(payload):
                    return None
                data_bytes = length
                index += length
            else:
                index = skip_field(payload, index, wire_type)
        except ValueError:
            return None

    if point_stride <= 0 or data_bytes <= 0:
        return None

    return PointCloudPayloadInfo(
        point_stride=point_stride,
        data_bytes=data_bytes,
        estimated_point_count=data_bytes // point_stride,
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

__all__ = [name for name in globals() if not name.startswith("__")]
