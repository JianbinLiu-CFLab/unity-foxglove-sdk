#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Decomposed ROS 2 CDR generator section."""

from __future__ import annotations

from .shared import *

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
        expected_repeated = REPEATED_TYPE_MAP.get(base_type)
        if expected_repeated and inner != expected_repeated:
            fail(f"pbc::RepeatedField<{expected_repeated}>")
        elif base_type == "geometry_msgs/Point" and inner not in {"global::Foxglove.Point3", "global::Foxglove.Vector3"}:
            fail("pbc::RepeatedField<global::Foxglove.Point3|Vector3>")
        elif base_type == "geometry_msgs/Vector3" and inner != "global::Foxglove.Vector3":
            fail("pbc::RepeatedField<global::Foxglove.Vector3>")
        elif base_type == "geometry_msgs/Quaternion" and inner != "global::Foxglove.Quaternion":
            fail("pbc::RepeatedField<global::Foxglove.Quaternion>")
        elif base_type == "geometry_msgs/Pose" and inner != "global::Foxglove.Pose":
            fail("pbc::RepeatedField<global::Foxglove.Pose>")
        elif base_type.startswith("foxglove_msgs/") and inner != f"global::Foxglove.{family}":
            fail(f"pbc::RepeatedField<global::Foxglove.{family}>")
        elif base_type in {"bool", "string", "builtin_interfaces/Time", "builtin_interfaces/Duration"}:
            fail(f"unsupported repeated {base_type} field type")
        return

    expected_scalar = dict(SCALAR_TYPE_MAP)
    expected_scalar.update({
        "builtin_interfaces/Time": "global::Google.Protobuf.WellKnownTypes.Timestamp",
        "builtin_interfaces/Duration": "global::Google.Protobuf.WellKnownTypes.Duration",
        "geometry_msgs/Quaternion": "global::Foxglove.Quaternion",
        "geometry_msgs/Vector3": "global::Foxglove.Vector3",
        "geometry_msgs/Pose": "global::Foxglove.Pose",
    })
    expected_scalar = expected_scalar.get(base_type)

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



__all__ = [name for name in globals() if not name.startswith("__")]
