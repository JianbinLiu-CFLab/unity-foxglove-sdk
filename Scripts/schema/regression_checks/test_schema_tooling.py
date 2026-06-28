#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for schema generation helper edge cases.

from __future__ import annotations

import contextlib
import importlib.util
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]


def load_module(name: str, relative: str):
    """Load one repository helper script as an isolated module."""
    path = ROOT / relative
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    original_path = list(sys.path)
    sys.path.insert(0, str(path.parent))
    try:
        spec.loader.exec_module(module)
    finally:
        sys.path[:] = original_path
    return module


class SchemaToolingTests(unittest.TestCase):
    """Regression coverage for schema helper tooling."""

    def test_cdr_generator_emits_null_guards_for_required_nested_geometry(self) -> None:
        """Generated nested geometry writers should not silently zero null values."""
        module = load_module("cdr_generator_under_test", "Scripts/schema/generate_ros2_cdr_serializers.py")

        schema = module.Schema(
            name="CompressedImage",
            schema_name="foxglove_msgs/msg/CompressedImage",
            source_file="CompressedImage.msg",
            fields=(
                module.Field(
                    ros_type="uint8[]",
                    name="data",
                    base_type="uint8",
                    array_kind="sequence",
                    fixed_length=None,
                    property_name="Data",
                    property_type="global::Google.Protobuf.ByteString",
                ),
            ),
        )
        generated = module.generate_serializers([schema])

        self.assertIn("if (value == null)", generated)
        self.assertIn("throw new ArgumentNullException(nameof(value));", generated)
        self.assertIn("var writer = new Ros2CdrWriter(256);", generated)
        self.assertIn("writer.WriteByteArray(message.Data.Span);", generated)
        self.assertIn("writer.WriteFloat64(value.W);", generated)
        self.assertNotIn("value?.W ?? 1.0", generated)

    def test_cdr_generator_supports_future_geometry_sequences(self) -> None:
        """Repeated Vector3 and Quaternion fields should not fail generation."""
        module = load_module("cdr_generator_geometry_sequences", "Scripts/schema/generate_ros2_cdr_serializers.py")

        vector_field = module.Field(
            ros_type="geometry_msgs/Vector3[]",
            name="linear_velocity",
            base_type="geometry_msgs/Vector3",
            array_kind="sequence",
            fixed_length=None,
            property_name="LinearVelocity",
            property_type="pbc::RepeatedField<global::Foxglove.Vector3>",
        )
        quaternion_field = module.Field(
            ros_type="geometry_msgs/Quaternion[]",
            name="orientation",
            base_type="geometry_msgs/Quaternion",
            array_kind="sequence",
            fixed_length=None,
            property_name="Orientation",
            property_type="pbc::RepeatedField<global::Foxglove.Quaternion>",
        )

        self.assertIn("WriteProtoVector3(writer, item);", "\n".join(module.writer_for_field(vector_field)))
        self.assertIn("ReadProtoVector3(reader)", "\n".join(module.reader_for_field(vector_field)))
        self.assertIn("WriteProtoQuaternion(writer, item);", "\n".join(module.writer_for_field(quaternion_field)))
        self.assertIn("ReadProtoQuaternion(reader)", "\n".join(module.reader_for_field(quaternion_field)))
        self.assertIn("new global::Foxglove.Vector3", "\n".join(module.sample_lines_for_field(vector_field, "Synthetic", 0)))
        self.assertIn("new global::Foxglove.Quaternion", "\n".join(module.sample_lines_for_field(quaternion_field, "Synthetic", 0)))

    def test_cdr_generator_supports_future_scalar_primitives(self) -> None:
        """Common ROS 2 primitive scalar fields should emit matching reader/writer calls."""
        module = load_module("cdr_generator_scalar_primitives", "Scripts/schema/generate_ros2_cdr_serializers.py")

        cases = {
            "int16": ("writer.WriteInt16((short)message.Value);", "reader.ReadInt16()"),
            "uint16": ("writer.WriteUInt16((ushort)message.Value);", "reader.ReadUInt16()"),
            "int32": ("writer.WriteInt32(message.Value);", "reader.ReadInt32()"),
            "int64": ("writer.WriteInt64(message.Value);", "reader.ReadInt64()"),
            "uint64": ("writer.WriteUInt64(message.Value);", "reader.ReadUInt64()"),
            "float32": ("writer.WriteFloat32(message.Value);", "reader.ReadFloat32()"),
        }

        for base_type, (writer_call, reader_call) in cases.items():
            with self.subTest(base_type=base_type):
                field = module.Field(
                    ros_type=base_type,
                    name="value",
                    base_type=base_type,
                    array_kind="scalar",
                    fixed_length=None,
                    property_name="Value",
                    property_type="float" if base_type == "float32" else ("long" if base_type == "int64" else ("ulong" if base_type == "uint64" else ("uint" if base_type == "uint16" else "int"))),
                )
                self.assertEqual([writer_call], module.writer_for_field(field))
                self.assertEqual([f"message.Value = {reader_call};"], module.reader_for_field(field))

    def test_cdr_generator_rejects_zero_length_fixed_samples(self) -> None:
        """Malformed fixed-length float64 fields should fail during generation."""
        module = load_module("cdr_generator_fixed_sample_guard", "Scripts/schema/generate_ros2_cdr_serializers.py")

        field = module.Field(
            ros_type="float64[0]",
            name="covariance",
            base_type="float64",
            array_kind="fixed",
            fixed_length=0,
            property_name="Covariance",
            property_type="pbc::RepeatedField<double>",
        )

        with self.assertRaisesRegex(RuntimeError, "positive length"):
            module.writer_for_field(field)
        with self.assertRaisesRegex(RuntimeError, "positive length"):
            module.reader_for_field(field)
        with self.assertRaisesRegex(RuntimeError, "positive length"):
            module.sample_lines_for_field(field, "Synthetic", 0)

    def test_schema_catalog_warns_when_source_commit_lookup_fails(self) -> None:
        """Source commit lookup should not silently swallow git failures."""
        module = load_module("schema_catalog_under_test", "Scripts/schema/generate_ros2_msg_schema_catalog.py")

        stderr = io.StringIO()
        with mock.patch.object(module.subprocess, "run", side_effect=PermissionError("denied")):
            with contextlib.redirect_stderr(stderr):
                commit = module.try_source_commit(ROOT / "third-party" / "foxglove" / "schemas")

        self.assertEqual("", commit)
        self.assertIn("warning", stderr.getvalue().lower())
        self.assertIn("source commit", stderr.getvalue().lower())

    def test_generated_output_validator_reports_stale_committed_files(self) -> None:
        """Fresh generator output should be byte-compared against committed files."""
        module = load_module("schema_generated_validator", "Scripts/schema/validate_schema_generated_outputs.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            committed = root / "Committed.cs"
            fresh = root / "Fresh.cs"
            committed.write_text("old\n", encoding="utf-8")
            fresh.write_text("new\n", encoding="utf-8")

            failures: list[str] = []
            module.compare_file(committed, fresh, failures)

        self.assertEqual(1, len(failures))
        self.assertIn("stale generated output", failures[0])


if __name__ == "__main__":
    unittest.main()
