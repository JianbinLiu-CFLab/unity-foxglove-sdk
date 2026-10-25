#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Decomposed ROS 2 CDR generator section."""

from __future__ import annotations

from .shared import *

def sample_scalar(field: Field, schema_name: str, index: int) -> str:
    """Generate a deterministic C# sample expression for one scalar field."""

    if field.base_type == "bool":
        return "true"
    if field.base_type in {"int16", "int32"}:
        return str(100 + index)
    if field.base_type in {"uint16", "uint32"}:
        return f"{100 + index}U"
    if field.base_type == "int64":
        return f"{100 + index}L"
    if field.base_type == "uint64":
        return f"{100 + index}UL"
    if field.base_type == "uint8":
        if field.property_type == "byte":
            return "(byte)1"
        return f"({field.property_type})1"
    if field.base_type == "float32":
        return f"{index + 1}.25f"
    if field.base_type == "float64":
        return f"{index + 1}.25"
    if field.base_type == "string":
        return csharp_string(f"{schema_name}.{field.name}")
    if field.base_type == "builtin_interfaces/Time":
        return f"new global::Google.Protobuf.WellKnownTypes.Timestamp {{ Seconds = 1700093000L, Nanos = {1000 + index} }}"
    if field.base_type == "builtin_interfaces/Duration":
        return f"new global::Google.Protobuf.WellKnownTypes.Duration {{ Seconds = {1 + index}, Nanos = {2000 + index} }}"
    if field.base_type == "geometry_msgs/Point":
        if field.property_type == "global::Foxglove.Point3":
            return f"new global::Foxglove.Point3 {{ X = {index + 1}.0, Y = {index + 2}.0, Z = {index + 3}.0 }}"
        return f"new global::Foxglove.Vector3 {{ X = {index + 1}.0, Y = {index + 2}.0, Z = {index + 3}.0 }}"
    if field.base_type == "geometry_msgs/Vector3":
        return f"new global::Foxglove.Vector3 {{ X = {index + 1}.0, Y = {index + 2}.0, Z = {index + 3}.0 }}"
    if field.base_type == "geometry_msgs/Quaternion":
        return "new global::Foxglove.Quaternion { X = 0.0, Y = 0.0, Z = 0.0, W = 1.0 }"
    if field.base_type == "geometry_msgs/Pose":
        return (
            "new global::Foxglove.Pose { Position = new global::Foxglove.Vector3 { X = 1.0, Y = 2.0, Z = 3.0 }, "
            "Orientation = new global::Foxglove.Quaternion { X = 0.0, Y = 0.0, Z = 0.0, W = 1.0 } }"
        )
    if field.base_type.startswith("foxglove_msgs/"):
        return f"Create{field.base_type.split('/', 1)[1]}Sample()"
    raise RuntimeError(f"Unsupported sample scalar: {field}")


def sample_lines_for_field(field: Field, schema_name: str, index: int) -> list[str]:
    """Generate deterministic C# sample assignment lines for one field."""

    if field.array_kind == "scalar":
        return [f"message.{field.property_name} = {sample_scalar(field, schema_name, index)};"]
    if field.base_type == "uint8":
        return [f"message.{field.property_name} = global::Google.Protobuf.ByteString.CopyFrom(new byte[] {{ 1, 2, 3, 4 }});"]
    if field.base_type in {"int16", "uint16", "int32", "uint32", "int64", "uint64", "float32"}:
        return [f"message.{field.property_name}.Add({sample_scalar(field, schema_name, index)});"]
    if field.base_type == "float64":
        if field.array_kind == "fixed" and (field.fixed_length is None or field.fixed_length <= 0):
            raise RuntimeError(f"Fixed float64 sample field must declare a positive length: {field}")
        count = field.fixed_length if field.array_kind == "fixed" else 2
        values = ", ".join(f"{i + 1}.0" for i in range(count))
        return [f"message.{field.property_name}.Add(new double[] {{ {values} }});"]
    if field.base_type == "geometry_msgs/Point":
        point = "new global::Foxglove.Point3 { X = 1.0, Y = 2.0, Z = 3.0 }"
        if repeated_inner(field.property_type) == "global::Foxglove.Vector3":
            point = "new global::Foxglove.Vector3 { X = 1.0, Y = 2.0, Z = 3.0 }"
        return [f"message.{field.property_name}.Add({point});"]
    if field.base_type == "geometry_msgs/Pose":
        return [f"message.{field.property_name}.Add({sample_scalar(field, schema_name, index)});"]
    if field.base_type == "geometry_msgs/Vector3":
        return [f"message.{field.property_name}.Add({sample_scalar(field, schema_name, index)});"]
    if field.base_type == "geometry_msgs/Quaternion":
        return [f"message.{field.property_name}.Add({sample_scalar(field, schema_name, index)});"]
    if field.base_type.startswith("foxglove_msgs/"):
        nested = field.base_type.split("/", 1)[1]
        return [f"message.{field.property_name}.Add(Create{nested}Sample());"]
    raise RuntimeError(f"Unsupported sample field: {field}")


def generate_samples(schemas: list[Schema]) -> str:
    """Generate the C# deterministic sample factory source."""

    lines: list[str] = [
        "// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.",
        "// SPDX-License-Identifier: Apache-2.0",
        "//",
        "// Module: Runtime/Schemas/Ros2Msg/Generated",
        "// Purpose: Generated deterministic ROS 2 CDR serializer samples.",
        "// Generated by Scripts/schema/generate_ros2_cdr_serializers.py.",
        "",
        f"namespace {CSHARP_NAMESPACE}",
        "{",
        "    internal static class Ros2CdrSampleFactory",
        "    {",
    ]
    for schema in schemas:
        lines.extend(
            [
                f"        internal static {schema_type(schema.name)} Create{schema.name}Sample()",
                "        {",
                f"            var message = new {schema_type(schema.name)}();",
            ]
        )
        for index, field in enumerate(schema.fields):
            for line in sample_lines_for_field(field, schema.name, index):
                lines.append("            " + line)
        lines.extend(["            return message;", "        }", ""])
    lines.extend(["    }", "}"])
    return "\n".join(lines) + "\n"


