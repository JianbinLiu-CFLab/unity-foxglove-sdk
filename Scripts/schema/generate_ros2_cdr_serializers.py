#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Generate direct ROS 2 CDR serializers for official Foxglove .msg schemas.
# Usage: python Scripts/schema/generate_ros2_cdr_serializers.py
# Inputs: third-party/foxglove-sdk/schemas/ros2/*.msg and generated Foxglove protobuf C# classes.
# Outputs: Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Ros2Msg/Generated/*.g.cs.
"""Generate direct ROS 2 CDR serializers for official Foxglove .msg schemas."""

from __future__ import annotations

import argparse
import hashlib
import re
from dataclasses import dataclass
from pathlib import Path

from generate_ros2_msg_schema_catalog import CATEGORIES, EXPECTED_FILE_COUNT


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_INPUT = REPO_ROOT / "third-party" / "foxglove-sdk" / "schemas" / "ros2"
PROTO_MESSAGES = (
    REPO_ROOT
    / "Packages"
    / "dev.unity2foxglove.sdk"
    / "Runtime"
    / "Schemas"
    / "Proto"
    / "Generated"
    / "Messages"
)
DEFAULT_OUTPUT_DIR = (
    REPO_ROOT
    / "Packages"
    / "dev.unity2foxglove.sdk"
    / "Runtime"
    / "Schemas"
    / "Proto"
    / "Ros2Msg"
    / "Generated"
)

PRIMITIVE_TYPES = {"bool", "uint8", "uint32", "float64", "string"}
STANDARD_TYPES = {
    "builtin_interfaces/Time",
    "builtin_interfaces/Duration",
    "geometry_msgs/Point",
    "geometry_msgs/Quaternion",
    "geometry_msgs/Vector3",
    "geometry_msgs/Pose",
}


@dataclass(frozen=True)
class Field:
    """Parsed ROS 2 field plus the generated protobuf property mapping."""

    ros_type: str
    name: str
    base_type: str
    array_kind: str
    fixed_length: int | None
    property_name: str
    property_type: str


@dataclass(frozen=True)
class Schema:
    """Parsed root Foxglove ROS 2 schema used for serializer generation."""

    name: str
    schema_name: str
    source_file: str
    fields: tuple[Field, ...]


def parse_args() -> argparse.Namespace:
    """Parse command-line paths for the generator."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
    return parser.parse_args()


def pascal_case(name: str) -> str:
    """Convert a ROS snake_case field name to the generated C# property name."""

    return "".join(part[:1].upper() + part[1:] for part in name.split("_"))


def split_ros_type(type_token: str) -> tuple[str, str, int | None]:
    """Split a ROS type token into base type, array kind, and fixed length."""

    match = re.match(r"^(?P<base>[^\[]+)(?:\[(?P<len>[0-9]*)\])?$", type_token)
    if not match:
        raise ValueError(f"Invalid ROS type token: {type_token}")
    base = match.group("base")
    length = match.group("len")
    if length is None:
        return base, "scalar", None
    if length == "":
        return base, "sequence", None
    return base, "fixed", int(length)


def parse_msg_fields(path: Path) -> list[tuple[str, str]]:
    """Read non-constant field declarations from a ROS 2 .msg file."""

    fields: list[tuple[str, str]] = []
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line or "=" in line:
            continue
        parts = line.split()
        if len(parts) < 2:
            continue
        fields.append((parts[0], parts[1]))
    return fields


def parse_properties(class_name: str) -> dict[str, str]:
    """Read public generated protobuf properties for a Foxglove CLR message."""

    path = PROTO_MESSAGES / f"{class_name}.cs"
    if not path.is_file():
        raise FileNotFoundError(f"Generated protobuf class not found: {path}")

    properties: dict[str, str] = {}
    text = path.read_text(encoding="utf-8")
    for match in re.finditer(r"^\s*public\s+([^\n{]+?)\s+([A-Z][A-Za-z0-9_]*)\s*\{\s*$", text, re.MULTILINE):
        type_name = " ".join(match.group(1).strip().split())
        prop_name = match.group(2)
        if prop_name in {"Descriptor", "Parser"}:
            continue
        if any(token in type_name for token in (" class", "enum", "static partial")):
            continue
        properties[prop_name] = type_name
    return properties


def repeated_inner(property_type: str) -> str | None:
    """Return the inner CLR type for a generated RepeatedField property."""

    match = re.match(r"pbc::RepeatedField<(.+)>$", property_type)
    return match.group(1) if match else None


def expected_property_family(base_type: str, array_kind: str) -> str:
    """Return the expected protobuf property family for a ROS 2 field type."""

    if base_type in PRIMITIVE_TYPES:
        return base_type
    if base_type.startswith("foxglove_msgs/"):
        return base_type.split("/", 1)[1]
    if base_type in STANDARD_TYPES:
        return base_type
    raise ValueError(f"Unknown ROS 2 field type: {base_type}")


def validate_property(schema_name: str, field_name: str, ros_type: str, base_type: str, array_kind: str, property_type: str) -> None:
    """Fail fast when a ROS 2 field does not match the generated CLR property."""

    family = expected_property_family(base_type, array_kind)
    inner = repeated_inner(property_type)

    def fail(expected: str) -> None:
        """Raise a detailed field-mapping failure."""

        raise RuntimeError(
            "[ros2-cdr-gen] field mapping failed:\n"
            f"schema=foxglove_msgs/msg/{schema_name}\n"
            f"field={field_name}\n"
            f"rosType={ros_type}\n"
            f"clrType=Foxglove.{schema_name}\n"
            f"attemptedProperty={pascal_case(field_name)}\n"
            f"expected={expected}\n"
            f"actual={property_type}"
        )

    if array_kind in {"sequence", "fixed"}:
        if base_type == "uint8":
            if property_type != "pb::ByteString":
                fail("pb::ByteString")
            return
        if inner is None:
            fail("pbc::RepeatedField<T>")
        if base_type == "uint32" and inner != "uint":
            fail("pbc::RepeatedField<uint>")
        elif base_type == "float64" and inner != "double":
            fail("pbc::RepeatedField<double>")
        elif base_type == "geometry_msgs/Point" and inner not in {"global::Foxglove.Point3", "global::Foxglove.Vector3"}:
            fail("pbc::RepeatedField<global::Foxglove.Point3|Vector3>")
        elif base_type == "geometry_msgs/Pose" and inner != "global::Foxglove.Pose":
            fail("pbc::RepeatedField<global::Foxglove.Pose>")
        elif base_type.startswith("foxglove_msgs/") and inner != f"global::Foxglove.{family}":
            fail(f"pbc::RepeatedField<global::Foxglove.{family}>")
        elif base_type in {"bool", "string", "builtin_interfaces/Time", "builtin_interfaces/Duration", "geometry_msgs/Quaternion", "geometry_msgs/Vector3"}:
            fail("supported repeated field type")
        return

    expected_scalar = {
        "bool": "bool",
        "uint32": "uint",
        "float64": "double",
        "string": "string",
        "builtin_interfaces/Time": "global::Google.Protobuf.WellKnownTypes.Timestamp",
        "builtin_interfaces/Duration": "global::Google.Protobuf.WellKnownTypes.Duration",
        "geometry_msgs/Quaternion": "global::Foxglove.Quaternion",
        "geometry_msgs/Vector3": "global::Foxglove.Vector3",
        "geometry_msgs/Pose": "global::Foxglove.Pose",
    }.get(base_type)

    if base_type == "uint8":
        if property_type != "byte" and not property_type.startswith(f"global::Foxglove.{schema_name}.Types."):
            fail("byte or generated enum")
    elif base_type == "geometry_msgs/Point":
        if property_type not in {"global::Foxglove.Point3", "global::Foxglove.Vector3"}:
            fail("global::Foxglove.Point3|Vector3")
    elif base_type.startswith("foxglove_msgs/"):
        if property_type != f"global::Foxglove.{family}":
            fail(f"global::Foxglove.{family}")
    elif expected_scalar and property_type != expected_scalar:
        fail(expected_scalar)


def load_schemas(input_dir: Path) -> list[Schema]:
    """Load and validate every root Foxglove ROS 2 schema from a directory."""

    files = sorted(input_dir.glob("*.msg"), key=lambda p: p.name)
    if len(files) != EXPECTED_FILE_COUNT:
        raise RuntimeError(f"Expected {EXPECTED_FILE_COUNT} ROS 2 .msg files, found {len(files)}")

    schemas: list[Schema] = []
    for path in files:
        class_name = path.stem
        properties = parse_properties(class_name)
        fields: list[Field] = []
        for ros_type, name in parse_msg_fields(path):
            base, array_kind, fixed_length = split_ros_type(ros_type)
            prop_name = pascal_case(name)
            if prop_name not in properties:
                raise RuntimeError(
                    "[ros2-cdr-gen] field mapping failed:\n"
                    f"schema=foxglove_msgs/msg/{class_name}\n"
                    f"field={name}\n"
                    f"rosType={ros_type}\n"
                    f"clrType=Foxglove.{class_name}\n"
                    f"attemptedProperty={prop_name}"
                )
            prop_type = properties[prop_name]
            validate_property(class_name, name, ros_type, base, array_kind, prop_type)
            fields.append(Field(ros_type, name, base, array_kind, fixed_length, prop_name, prop_type))
        schemas.append(
            Schema(
                name=class_name,
                schema_name=f"foxglove_msgs/msg/{class_name}",
                source_file=path.name,
                fields=tuple(fields),
            )
        )
    return schemas


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
    if field.base_type == "uint8":
        return [f"writer.WriteUInt8((byte){access});"]
    if field.base_type == "uint32":
        return [f"writer.WriteUInt32({access});"]
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
        return [f"writer.WriteByteArray({access}?.ToByteArray());"]
    if field.array_kind == "fixed":
        return [f"writer.WriteFloat64Fixed({access}, {field.fixed_length}, {csharp_string(field.name)});"]
    if field.base_type == "float64":
        return [f"writer.WriteFloat64Sequence({access});"]
    if field.base_type == "uint32":
        return [f"writer.WriteUInt32Sequence({access});"]

    item_writer = None
    if field.base_type == "geometry_msgs/Point":
        item_writer = "WriteProtoPoint(writer, item);"
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


def generate_serializers(schemas: list[Schema]) -> str:
    """Generate the C# per-schema direct CDR serializer source."""

    lines: list[str] = [
        "// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.",
        "// SPDX-License-Identifier: Apache-2.0",
        "//",
        "// Module: Runtime/Schemas/Proto/Ros2Msg/Generated",
        "// Purpose: Generated direct ROS 2 CDR serializers for official Foxglove .msg schemas.",
        "// Generated by Scripts/schema/generate_ros2_cdr_serializers.py.",
        "",
        "using System;",
        "",
        "namespace Unity.FoxgloveSDK.Schemas.Ros2Msg",
        "{",
        "    /// <summary>Generated direct ROS 2 CDR serializers for official Foxglove .msg schemas.</summary>",
        "    public static class Ros2CdrGeneratedSerializers",
        "    {",
        "        private static void WriteProtoPoint(Ros2CdrWriter writer, global::Foxglove.Point3 value)",
        "        {",
        "            if (writer == null)",
        "                throw new ArgumentNullException(nameof(writer));",
        "",
        "            writer.WriteFloat64(value?.X ?? 0.0);",
        "            writer.WriteFloat64(value?.Y ?? 0.0);",
        "            writer.WriteFloat64(value?.Z ?? 0.0);",
        "        }",
        "",
        "        private static void WriteProtoVector3(Ros2CdrWriter writer, global::Foxglove.Vector3 value)",
        "        {",
        "            if (writer == null)",
        "                throw new ArgumentNullException(nameof(writer));",
        "",
        "            writer.WriteFloat64(value?.X ?? 0.0);",
        "            writer.WriteFloat64(value?.Y ?? 0.0);",
        "            writer.WriteFloat64(value?.Z ?? 0.0);",
        "        }",
        "",
        "        private static void WriteProtoQuaternion(Ros2CdrWriter writer, global::Foxglove.Quaternion value)",
        "        {",
        "            if (writer == null)",
        "                throw new ArgumentNullException(nameof(writer));",
        "",
        "            writer.WriteFloat64(value?.X ?? 0.0);",
        "            writer.WriteFloat64(value?.Y ?? 0.0);",
        "            writer.WriteFloat64(value?.Z ?? 0.0);",
        "            writer.WriteFloat64(value?.W ?? 1.0);",
        "        }",
        "",
        "        private static void WriteProtoPose(Ros2CdrWriter writer, global::Foxglove.Pose value)",
        "        {",
        "            if (writer == null)",
        "                throw new ArgumentNullException(nameof(writer));",
        "",
        "            WriteProtoVector3(writer, value?.Position);",
        "            WriteProtoQuaternion(writer, value?.Orientation);",
        "        }",
        "",
    ]

    for schema in schemas:
        lines.extend(
            [
                f"        public static byte[] Serialize({schema_type(schema.name)} message)",
                "        {",
                "            if (message == null)",
                "                throw new ArgumentNullException(nameof(message));",
                "            var writer = new Ros2CdrWriter();",
                f"            Write{schema.name}(writer, message);",
                "            return writer.ToArray();",
                "        }",
                "",
            ]
        )

    for schema in schemas:
        lines.extend(
            [
                f"        internal static void Write{schema.name}(Ros2CdrWriter writer, {schema_type(schema.name)} message)",
                "        {",
                "            if (writer == null)",
                "                throw new ArgumentNullException(nameof(writer));",
                f"            message ??= new {schema_type(schema.name)}();",
                "",
            ]
        )
        for field in schema.fields:
            for line in writer_for_field(field):
                lines.append("            " + line)
        lines.extend(["        }", ""])

    lines.extend(["    }", "}"])
    return "\n".join(lines) + "\n"


def sample_scalar(field: Field, schema_name: str, index: int) -> str:
    """Generate a deterministic C# sample expression for one scalar field."""

    if field.base_type == "bool":
        return "true"
    if field.base_type == "uint8":
        if field.property_type == "byte":
            return "(byte)1"
        return f"({field.property_type})1"
    if field.base_type == "uint32":
        return f"{100 + index}U"
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
    if field.base_type == "uint32":
        return [f"message.{field.property_name}.Add(1U);", f"message.{field.property_name}.Add(2U);"]
    if field.base_type == "float64":
        count = field.fixed_length if field.array_kind == "fixed" else 2
        values = ", ".join(f"{i + 1}.0" for i in range(count or 0))
        return [f"message.{field.property_name}.Add(new double[] {{ {values} }});"]
    if field.base_type == "geometry_msgs/Point":
        point = "new global::Foxglove.Point3 { X = 1.0, Y = 2.0, Z = 3.0 }"
        if repeated_inner(field.property_type) == "global::Foxglove.Vector3":
            point = "new global::Foxglove.Vector3 { X = 1.0, Y = 2.0, Z = 3.0 }"
        return [f"message.{field.property_name}.Add({point});"]
    if field.base_type == "geometry_msgs/Pose":
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
        "// Module: Runtime/Schemas/Proto/Ros2Msg/Generated",
        "// Purpose: Generated deterministic ROS 2 CDR serializer samples.",
        "// Generated by Scripts/schema/generate_ros2_cdr_serializers.py.",
        "",
        "namespace Unity.FoxgloveSDK.Schemas.Ros2Msg",
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


def generate_registry(schemas: list[Schema]) -> str:
    """Generate the C# serializer registry source."""

    entry_lines: list[str] = []
    for schema in schemas:
        category = CATEGORIES.get(schema.name, "")
        entry_lines.append(
            "            new Ros2CdrSerializerEntry(\n"
            f"                {csharp_string(schema.schema_name)},\n"
            f"                typeof({schema_type(schema.name)}),\n"
            f"                {csharp_string(category)},\n"
            f"                {csharp_string(schema.source_file)},\n"
            f"                message => Ros2CdrGeneratedSerializers.Serialize(({schema_type(schema.name)})message),\n"
            f"                () => Ros2CdrSampleFactory.Create{schema.name}Sample())"
        )
    entries = ",\n".join(entry_lines)
    return f"""// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Ros2Msg/Generated
// Purpose: Generated ROS 2 CDR serializer registry.
// Generated by Scripts/schema/generate_ros2_cdr_serializers.py.

using System;
using System.Collections.Generic;
using Google.Protobuf;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{{
    /// <summary>One generated ROS 2 CDR serializer registry entry.</summary>
    public sealed class Ros2CdrSerializerEntry
    {{
        private readonly Func<IMessage, byte[]> _serializer;
        private readonly Func<IMessage> _sampleFactory;

        internal Ros2CdrSerializerEntry(
            string schemaName,
            Type clrType,
            string category,
            string sourceFile,
            Func<IMessage, byte[]> serializer,
            Func<IMessage> sampleFactory)
        {{
            SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
            ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
            Category = category ?? string.Empty;
            SourceFile = sourceFile ?? string.Empty;
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _sampleFactory = sampleFactory ?? throw new ArgumentNullException(nameof(sampleFactory));
        }}

        public string SchemaName {{ get; }}
        public Type ClrType {{ get; }}
        public string Category {{ get; }}
        public string SourceFile {{ get; }}
        public bool HasDeterministicSample => _sampleFactory != null;

        public byte[] Serialize(IMessage message)
        {{
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (!ClrType.IsInstanceOfType(message))
                throw new InvalidOperationException($\"Schema '{{SchemaName}}' expects CLR type '{{ClrType.FullName}}', got '{{message.GetType().FullName}}'.\");
            return _serializer(message);
        }}

        public IMessage CreateSample()
        {{
            return _sampleFactory();
        }}
    }}

    /// <summary>Generated registry of direct ROS 2 CDR serializers for official Foxglove .msg schemas.</summary>
    public static class Ros2CdrSerializerRegistry
    {{
        public const int SerializerCount = {EXPECTED_FILE_COUNT};

        private static readonly Ros2CdrSerializerEntry[] EntriesArray =
        {{
{entries}
        }};

        private static readonly Dictionary<string, Ros2CdrSerializerEntry> BySchemaName = BuildSchemaNameMap();
        private static readonly Dictionary<Type, Ros2CdrSerializerEntry> ByClrType = BuildClrTypeMap();

        public static IReadOnlyList<Ros2CdrSerializerEntry> Entries {{ get; }} = Array.AsReadOnly(EntriesArray);

        public static bool TryGetBySchemaName(string schemaName, out Ros2CdrSerializerEntry entry)
        {{
            if (schemaName == null)
            {{
                entry = null;
                return false;
            }}
            return BySchemaName.TryGetValue(schemaName, out entry);
        }}

        public static bool TryGetByClrType(Type clrType, out Ros2CdrSerializerEntry entry)
        {{
            if (clrType == null)
            {{
                entry = null;
                return false;
            }}
            return ByClrType.TryGetValue(clrType, out entry);
        }}

        /// <summary>
        /// Try to serialize a message. Returns false only for an unknown schema or CLR type mismatch;
        /// malformed messages still throw, for example wrong fixed-array lengths.
        /// </summary>
        public static bool TrySerialize(string schemaName, IMessage message, out byte[] payload)
        {{
            payload = null;
            if (!TryGetBySchemaName(schemaName, out var entry))
                return false;
            if (message == null || !entry.ClrType.IsInstanceOfType(message))
                return false;
            payload = entry.Serialize(message);
            return true;
        }}

        public static byte[] Serialize(string schemaName, IMessage message)
        {{
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (!TryGetBySchemaName(schemaName, out var entry))
                throw new InvalidOperationException(\"Unknown ROS 2 CDR serializer schema: \" + schemaName);
            return entry.Serialize(message);
        }}

        private static Dictionary<string, Ros2CdrSerializerEntry> BuildSchemaNameMap()
        {{
            var map = new Dictionary<string, Ros2CdrSerializerEntry>(StringComparer.Ordinal);
            foreach (var entry in EntriesArray)
                map.Add(entry.SchemaName, entry);
            return map;
        }}

        private static Dictionary<Type, Ros2CdrSerializerEntry> BuildClrTypeMap()
        {{
            var map = new Dictionary<Type, Ros2CdrSerializerEntry>();
            foreach (var entry in EntriesArray)
                map.Add(entry.ClrType, entry);
            return map;
        }}
    }}
}}
"""


def meta_guid(relative_path: str) -> str:
    """Generate a deterministic Unity .meta GUID for a generated asset."""

    return hashlib.md5(("unity2foxglove-phase93:" + relative_path.replace("\\", "/")).encode("utf-8")).hexdigest()


def existing_guid(path: Path) -> str | None:
    """Read an existing Unity .meta GUID when regenerating in place."""

    if not path.is_file():
        return None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("guid:"):
            return line.split(":", 1)[1].strip()
    return None


def write_text(path: Path, text: str) -> None:
    """Write UTF-8 text with LF line endings, creating parent directories."""

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def write_meta(path: Path, is_folder: bool) -> None:
    """Write a deterministic Unity .meta file for a generated file or folder."""

    rel = str(path.relative_to(REPO_ROOT))
    meta = Path(str(path) + ".meta")
    guid = existing_guid(meta) or meta_guid(rel)
    if is_folder:
        text = f"""fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""
    else:
        text = f"""fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""
    write_text(meta, text)


def generate(input_dir: Path, output_dir: Path) -> str:
    """Generate serializers, registry, samples, and Unity .meta files."""

    schemas = load_schemas(input_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    write_meta(output_dir, is_folder=True)

    outputs = {
        "Ros2CdrGeneratedSerializers.g.cs": generate_serializers(schemas),
        "Ros2CdrSerializerRegistry.g.cs": generate_registry(schemas),
        "Ros2CdrSampleFactory.g.cs": generate_samples(schemas),
    }
    for name, text in outputs.items():
        path = output_dir / name
        write_text(path, text)
        write_meta(path, is_folder=False)

    for name in outputs:
        if not Path(str(output_dir / name) + ".meta").is_file():
            raise RuntimeError(f"Missing generated Unity .meta file: {name}.meta")
    if not Path(str(output_dir) + ".meta").is_file():
        raise RuntimeError(f"Missing generated Unity .meta file: {output_dir}.meta")

    return f"[ros2-cdr-gen] generated {len(schemas)} serializers in {output_dir}"


def main() -> int:
    """Run the generator and print a concise summary."""

    args = parse_args()
    print(generate(args.input, args.output_dir))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
