#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Decomposed ROS 2 CDR generator section."""

from __future__ import annotations

from .shared import *

def csharp_string(value: str) -> str:
    """Escape a Python string as a C# string literal."""

    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def schema_type(name: str) -> str:
    """Return a fully-qualified generated Foxglove protobuf CLR type."""

    return f"global::Foxglove.{name}"


def writer_for_scalar(field: Field, message_expr: str) -> list[str]:
    """Generate C# writer statements for one scalar field."""

    access = f"{message_expr}.{field.property_name}"
    if field.base_type == "bool":
        return [f"writer.WriteBool({access});"]
    if field.base_type == "int16":
        return [f"writer.WriteInt16((short){access});"]
    if field.base_type == "uint16":
        return [f"writer.WriteUInt16((ushort){access});"]
    if field.base_type == "int32":
        return [f"writer.WriteInt32({access});"]
    if field.base_type == "uint8":
        return [f"writer.WriteUInt8((byte){access});"]
    if field.base_type == "uint32":
        return [f"writer.WriteUInt32({access});"]
    if field.base_type == "int64":
        return [f"writer.WriteInt64({access});"]
    if field.base_type == "uint64":
        return [f"writer.WriteUInt64({access});"]
    if field.base_type == "float32":
        return [f"writer.WriteFloat32({access});"]
    if field.base_type == "float64":
        return [f"writer.WriteFloat64({access});"]
    if field.base_type == "string":
        return [f"writer.WriteString({access});"]
    if field.base_type == "builtin_interfaces/Time":
        return [f"Ros2CdrGeometryWriter.WriteTime(writer, {access});"]
    if field.base_type == "builtin_interfaces/Duration":
        return [f"Ros2CdrGeometryWriter.WriteDuration(writer, {access});"]
    if field.base_type == "geometry_msgs/Point":
        return [f"WriteProtoPoint(writer, {access});"]
    if field.base_type == "geometry_msgs/Vector3":
        return [f"WriteProtoVector3(writer, {access});"]
    if field.base_type == "geometry_msgs/Quaternion":
        return [f"WriteProtoQuaternion(writer, {access});"]
    if field.base_type == "geometry_msgs/Pose":
        return [f"WriteProtoPose(writer, {access});"]
    if field.base_type.startswith("foxglove_msgs/"):
        return [f"Write{field.base_type.split('/', 1)[1]}(writer, {access});"]
    raise RuntimeError(f"Unsupported scalar field: {field}")


def writer_for_field(field: Field) -> list[str]:
    """Generate C# writer statements for one scalar, sequence, or fixed field."""

    access = f"message.{field.property_name}"
    if field.array_kind == "scalar":
        return writer_for_scalar(field, "message")
    if field.base_type == "uint8":
        return [f"writer.WriteByteArray({access}.Span);"]
    if field.array_kind == "fixed":
        if not field.fixed_length or field.fixed_length <= 0:
            raise RuntimeError(f"Fixed array field must declare a positive length: {field}")
        if field.base_type != "float64":
            raise RuntimeError(f"Unsupported fixed array base type '{field.base_type}': {field}")
        return [f"writer.WriteFloat64Fixed({access}, {field.fixed_length}, {csharp_string(field.name)});"]
    if field.base_type == "float64":
        return [f"writer.WriteFloat64Sequence({access});"]
    if field.base_type == "uint32":
        return [f"writer.WriteUInt32Sequence({access});"]

    item_writer = None
    if field.base_type == "geometry_msgs/Point":
        item_writer = "WriteProtoPoint(writer, item);"
    elif field.base_type == "geometry_msgs/Vector3":
        item_writer = "WriteProtoVector3(writer, item);"
    elif field.base_type == "geometry_msgs/Quaternion":
        item_writer = "WriteProtoQuaternion(writer, item);"
    elif field.base_type == "geometry_msgs/Pose":
        item_writer = "WriteProtoPose(writer, item);"
    elif field.base_type.startswith("foxglove_msgs/"):
        item_writer = f"Write{field.base_type.split('/', 1)[1]}(writer, item);"
    if item_writer is None:
        raise RuntimeError(f"Unsupported sequence field: {field}")

    return [
        f"writer.WriteSequenceLength({access}?.Count ?? 0);",
        f"if ({access} != null)",
        "{",
        f"    foreach (var item in {access})",
        f"        {item_writer}",
        "}",
    ]


def fixed_size_floor(field: Field) -> int:
    """Return a conservative fixed-size byte floor for a CDR field."""

    primitive_sizes = {
        "bool": 1,
        "uint8": 1,
        "int16": 2,
        "uint16": 2,
        "int32": 4,
        "uint32": 4,
        "float32": 4,
        "int64": 8,
        "uint64": 8,
        "float64": 8,
        "builtin_interfaces/Time": 8,
        "builtin_interfaces/Duration": 8,
        "geometry_msgs/Point": 24,
        "geometry_msgs/Vector3": 24,
        "geometry_msgs/Quaternion": 32,
        "geometry_msgs/Pose": 56,
    }
    base = primitive_sizes.get(field.base_type, 64 if field.base_type.startswith("foxglove_msgs/") else 0)
    if field.array_kind == "fixed" and field.fixed_length:
        return 4 + (base * field.fixed_length)
    if field.array_kind == "sequence":
        if field.base_type == "uint8":
            return 512
        if field.base_type.startswith("foxglove_msgs/") or field.base_type.startswith("geometry_msgs/"):
            return 1024
        return 4 + (base * 8)
    if field.base_type == "string":
        return 64
    return base


def capacity_hint_for_schema(schema: Schema) -> int:
    """Estimate an initial CDR writer capacity that avoids obvious resize churn."""

    # Includes the four-byte CDR encapsulation header plus modest per-field padding.
    floor = 4
    for field in schema.fields:
        floor += fixed_size_floor(field) + 8

    floor = max(64, floor)
    return ((floor + 15) // 16) * 16


def reader_for_scalar(field: Field) -> str:
    """Generate a C# reader expression for one scalar field."""

    if field.base_type == "bool":
        return "reader.ReadBool()"
    if field.base_type == "int16":
        return "reader.ReadInt16()"
    if field.base_type == "uint16":
        return "reader.ReadUInt16()"
    if field.base_type == "int32":
        return "reader.ReadInt32()"
    if field.base_type == "uint8":
        if field.property_type == "byte":
            return "reader.ReadUInt8()"
        return f"({field.property_type})reader.ReadUInt8()"
    if field.base_type == "uint32":
        return "reader.ReadUInt32()"
    if field.base_type == "int64":
        return "reader.ReadInt64()"
    if field.base_type == "uint64":
        return "reader.ReadUInt64()"
    if field.base_type == "float32":
        return "reader.ReadFloat32()"
    if field.base_type == "float64":
        return "reader.ReadFloat64()"
    if field.base_type == "string":
        return "reader.ReadString()"
    if field.base_type == "builtin_interfaces/Time":
        return "ReadTime(reader)"
    if field.base_type == "builtin_interfaces/Duration":
        return "ReadDuration(reader)"
    if field.base_type == "geometry_msgs/Point":
        return "ReadProtoPoint(reader)" if field.property_type == "global::Foxglove.Point3" else "ReadProtoVector3(reader)"
    if field.base_type == "geometry_msgs/Vector3":
        return "ReadProtoVector3(reader)"
    if field.base_type == "geometry_msgs/Quaternion":
        return "ReadProtoQuaternion(reader)"
    if field.base_type == "geometry_msgs/Pose":
        return "ReadProtoPose(reader)"
    if field.base_type.startswith("foxglove_msgs/"):
        return f"Read{field.base_type.split('/', 1)[1]}(reader)"
    raise RuntimeError(f"Unsupported scalar field: {field}")


def reader_for_field(field: Field) -> list[str]:
    """Generate C# reader statements for one scalar, sequence, or fixed field."""

    access = f"message.{field.property_name}"
    if field.array_kind == "scalar":
        return [f"{access} = {reader_for_scalar(field)};"]
    if field.base_type == "uint8":
        return [f"{access} = global::Google.Protobuf.ByteString.CopyFrom(reader.ReadByteArray());"]
    if field.array_kind == "fixed":
        if not field.fixed_length or field.fixed_length <= 0:
            raise RuntimeError(f"Fixed array field must declare a positive length: {field}")
        if field.base_type != "float64":
            raise RuntimeError(f"Unsupported fixed array base type '{field.base_type}': {field}")
        return [f"{access}.Add(reader.ReadFloat64Fixed({field.fixed_length}));"]
    if field.base_type == "float64":
        return [f"{access}.Add(reader.ReadFloat64Sequence());"]
    if field.base_type == "uint32":
        return [f"{access}.Add(reader.ReadUInt32Sequence());"]

    item_reader = None
    if field.base_type == "geometry_msgs/Point":
        item_reader = "ReadProtoPoint(reader)" if repeated_inner(field.property_type) == "global::Foxglove.Point3" else "ReadProtoVector3(reader)"
    elif field.base_type == "geometry_msgs/Vector3":
        item_reader = "ReadProtoVector3(reader)"
    elif field.base_type == "geometry_msgs/Quaternion":
        item_reader = "ReadProtoQuaternion(reader)"
    elif field.base_type == "geometry_msgs/Pose":
        item_reader = "ReadProtoPose(reader)"
    elif field.base_type.startswith("foxglove_msgs/"):
        item_reader = f"Read{field.base_type.split('/', 1)[1]}(reader)"
    if item_reader is None:
        raise RuntimeError(f"Unsupported sequence field: {field}")

    count_name = f"{field.property_name}Count"
    return [
        f"var {count_name} = reader.ReadSequenceLength();",
        f"for (var i = 0; i < {count_name}; i++)",
        f"    {access}.Add({item_reader});",
    ]


