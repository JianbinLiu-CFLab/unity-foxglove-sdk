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

    def test_schema_generators_default_to_bridge_package(self) -> None:
        """Default generation must not recreate ROS-owned sources in the core SDK."""
        catalog = load_module(
            "schema_catalog_default_output",
            "Scripts/schema/generate_ros2_msg_schema_catalog.py",
        )
        cdr = load_module(
            "cdr_generator_default_output",
            "Scripts/schema/generate_ros2_cdr_serializers.py",
        )
        bridge_root = (
            ROOT
            / "Packages"
            / "dev.unity2foxglove.ros2bridge"
            / "Runtime"
            / "Schemas"
            / "Ros2Msg"
        )

        self.assertEqual(
            bridge_root / "FoxgloveRos2MsgSchemaCatalog.cs",
            catalog.DEFAULT_OUTPUT,
        )
        self.assertEqual(bridge_root / "Generated", cdr.DEFAULT_OUTPUT_DIR)

    def test_schema_generators_emit_bridge_owned_namespace(self) -> None:
        """Generated ROS schema/CDR types must remain owned by the Bridge package."""
        catalog = load_module(
            "schema_catalog_bridge_namespace",
            "Scripts/schema/generate_ros2_msg_schema_catalog.py",
        )
        cdr = load_module(
            "cdr_generator_bridge_namespace",
            "Scripts/schema/generate_ros2_cdr_serializers.py",
        )
        expected = "namespace Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg"

        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / "FoxgloveRos2MsgSchemaCatalog.cs"
            catalog.generate(catalog.DEFAULT_INPUT, output)
            self.assertIn(expected, output.read_text(encoding="utf-8"))

        generated_cdr = (
            cdr.generate_serializers([]),
            cdr.generate_deserializers([]),
            cdr.generate_samples([]),
            cdr.generate_registry([]),
            cdr.generate_deserializer_registry([]),
        )
        for source in generated_cdr:
            self.assertIn(expected, source)

    def test_cdr_generator_emits_null_guards_for_required_nested_geometry(self) -> None:
        """Generated nested geometry writers should not silently zero null values."""
        module = load_module("cdr_generator_under_test", "Scripts/schema/generate_ros2_cdr_serializers.py")

        schema = module.Schema(
            name="QuaternionStamped",
            schema_name="geometry_msgs/msg/QuaternionStamped",
            source_file="QuaternionStamped.msg",
            fields=(
                module.Field(
                    ros_type="geometry_msgs/Quaternion",
                    name="orientation",
                    base_type="geometry_msgs/Quaternion",
                    array_kind="scalar",
                    fixed_length=None,
                    property_name="Orientation",
                    property_type="global::Foxglove.Quaternion",
                ),
            ),
        )
        generated = module.generate_serializers([schema])

        self.assertIn("if (value == null)", generated)
        self.assertIn("throw new ArgumentNullException(nameof(value));", generated)
        self.assertIn("var writer = new Ros2CdrWriter(64);", generated)
        self.assertIn("WriteProtoQuaternion(writer, message.Orientation);", generated)
        self.assertIn("writer.WriteFloat64(value.W);", generated)
        for method in ("WriteProtoPoint", "WriteProtoVector3", "WriteProtoQuaternion", "WriteProtoPose"):
            start = generated.index(f"private static void {method}")
            end = generated.find("\n        private static", start + 1)
            body = generated[start:] if end < 0 else generated[start:end]
            self.assertNotIn("value?.", body)
            self.assertIn("if (value == null)", body)
            self.assertIn("throw new ArgumentNullException(nameof(value));", body)
        self.assertNotIn("value?.W ?? 1.0", generated)

    def test_cdr_generator_byte_array_capacity_is_schema_specific(self) -> None:
        """Byte-array schemas should retain their bounded writer capacity and data write."""
        module = load_module("cdr_generator_byte_array_under_test", "Scripts/schema/generate_ros2_cdr_serializers.py")

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

        self.assertIn("var writer = new Ros2CdrWriter(528);", generated)
        self.assertIn("writer.WriteByteArray(message.Data.Span);", generated)

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

    def test_cdr_generator_rejects_unsupported_fixed_array_base_types(self) -> None:
        """Fixed arrays must fail closed until each base type has explicit CDR support."""
        module = load_module("cdr_generator_fixed_array_type_guard", "Scripts/schema/generate_ros2_cdr_serializers.py")

        field = module.Field(
            ros_type="int32[4]",
            name="indices",
            base_type="int32",
            array_kind="fixed",
            fixed_length=4,
            property_name="Indices",
            property_type="pbc::RepeatedField<int>",
        )

        with self.assertRaisesRegex(RuntimeError, "Unsupported fixed array base type"):
            module.writer_for_field(field)
        with self.assertRaisesRegex(RuntimeError, "Unsupported fixed array base type"):
            module.reader_for_field(field)

    def test_cdr_generator_reports_unsupported_repeated_fields(self) -> None:
        """Unsupported repeated field diagnostics should not imply support."""
        module = load_module("cdr_generator_repeated_error_text", "Scripts/schema/generate_ros2_cdr_serializers.py")

        with self.assertRaisesRegex(RuntimeError, "unsupported repeated bool field type"):
            module.validate_property(
                "Synthetic",
                "flags",
                "bool[]",
                "bool",
                "sequence",
                "pbc::RepeatedField<bool>")

    def test_cdr_generator_try_deserialize_documents_and_catches_malformed_payloads(self) -> None:
        """TryDeserialize should have no-throw semantics for malformed payloads."""
        module = load_module("cdr_generator_try_deserialize_guard", "Scripts/schema/generate_ros2_cdr_serializers.py")

        schema = module.Schema(
            name="CompressedImage",
            schema_name="foxglove_msgs/msg/CompressedImage",
            source_file="CompressedImage.msg",
            fields=(),
        )
        generated = module.generate_deserializer_registry([schema])

        self.assertIn("malformed CDR payloads", generated)
        self.assertIn("if (payload == null)", generated)
        self.assertIn("catch (Exception)", generated)
        self.assertIn("message = null;", generated)

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

    def test_schema_catalog_reports_missing_schema_names(self) -> None:
        """Snapshot validation should name missing and extra .msg files."""
        module = load_module("schema_catalog_missing_files", "Scripts/schema/generate_ros2_msg_schema_catalog.py")

        files = [Path(name + ".msg") for name in sorted(module.EXPECTED_SCHEMA_NAMES - {"Vector2"})]
        files.append(Path("Unexpected.msg"))

        with self.assertRaisesRegex(RuntimeError, "Vector2.msg.*Unexpected.msg"):
            module.validate_schema_files(files, Path("schemas"))

    def test_schema_catalog_rejects_circular_dependencies(self) -> None:
        """Merged schema generation should fail on message dependency cycles."""
        module = load_module("schema_catalog_cycle_guard", "Scripts/schema/generate_ros2_msg_schema_catalog.py")
        sources = {
            "A": "foxglove_msgs/B child\n",
            "B": "foxglove_msgs/A parent\n",
        }

        with self.assertRaisesRegex(ValueError, "Circular ROS 2 .msg dependency"):
            module.merged_schema(sources["A"], sources, root_name="A")

    def test_generators_skip_identical_text_writes(self) -> None:
        """Schema generators should not churn generated file mtimes when text is unchanged."""
        cdr = load_module("cdr_generator_write_cache", "Scripts/schema/generate_ros2_cdr_serializers.py")
        catalog = load_module("schema_catalog_write_cache", "Scripts/schema/generate_ros2_msg_schema_catalog.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            cdr_output = root / "Ros2CdrGeneratedSerializers.g.cs"
            catalog_output = root / "FoxgloveRos2MsgSchemaCatalog.cs"
            cdr_output.write_text("same\n", encoding="utf-8")
            catalog_output.write_text("same\n", encoding="utf-8")
            cdr_mtime = cdr_output.stat().st_mtime_ns
            catalog_mtime = catalog_output.stat().st_mtime_ns

            cdr.write_text(cdr_output, "same\n")
            catalog.write_text_if_changed(catalog_output, "same\n")

            self.assertEqual(cdr_mtime, cdr_output.stat().st_mtime_ns)
            self.assertEqual(catalog_mtime, catalog_output.stat().st_mtime_ns)

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

    def test_generated_output_validator_uses_current_python_and_timeout(self) -> None:
        """Generator validation should not depend on a bare python executable or hang forever."""
        module = load_module("schema_generated_validator_commands", "Scripts/schema/validate_schema_generated_outputs.py")
        calls: list[list[str]] = []

        def fake_run_generator(command: list[str]) -> None:
            """Capture generator commands and mirror committed outputs into the temp target."""
            calls.append(command)
            output_index = command.index("--output") + 1 if "--output" in command else -1
            output_dir_index = command.index("--output-dir") + 1 if "--output-dir" in command else -1
            if output_index > 0:
                Path(command[output_index]).write_bytes(module.COMMITTED_CATALOG.read_bytes())
            if output_dir_index > 0:
                output_dir = Path(command[output_dir_index])
                output_dir.mkdir(parents=True, exist_ok=True)
                for name in module.EXPECTED_CDR_SOURCES:
                    (output_dir / name).write_bytes((module.COMMITTED_CDR_DIR / name).read_bytes())

        with (
            mock.patch.object(module, "schema_snapshot_available", return_value=True),
            mock.patch.object(module, "run_generator", side_effect=fake_run_generator),
        ):
            failures = module.validate_generated_outputs()

        self.assertEqual([], failures)
        self.assertEqual(2, len(calls))
        self.assertTrue(all(command[0] == sys.executable for command in calls))
        self.assertEqual(120, module.GENERATOR_TIMEOUT_SECONDS)

    def test_generated_output_validator_accepts_a_source_only_checkout(self) -> None:
        """A clean worktree should validate committed generated inventory without an untracked SDK clone."""
        module = load_module("schema_generated_validator_source_only", "Scripts/schema/validate_schema_generated_outputs.py")

        with (
            mock.patch.object(module, "schema_snapshot_available", return_value=False),
            mock.patch.object(module, "run_generator") as run_generator,
        ):
            failures = module.validate_generated_outputs()

        self.assertEqual([], failures)
        run_generator.assert_not_called()

    def test_generated_output_validator_rejects_missing_committed_catalog_without_snapshot(self) -> None:
        """Source-only validation must still fail closed when committed generated output is absent."""
        module = load_module("schema_generated_validator_missing_catalog", "Scripts/schema/validate_schema_generated_outputs.py")

        with tempfile.TemporaryDirectory() as temp:
            missing_catalog = Path(temp) / "FoxgloveRos2MsgSchemaCatalog.cs"
            with mock.patch.object(module, "schema_snapshot_available", return_value=False), mock.patch.object(
                module, "COMMITTED_CATALOG", missing_catalog
            ):
                failures = module.validate_generated_outputs()

        self.assertIn("missing committed file", "\n".join(failures))

    def test_generated_output_validator_reports_startup_errors(self) -> None:
        """Missing generator executables should produce clean failure messages."""
        module = load_module("schema_generated_validator_oserror", "Scripts/schema/validate_schema_generated_outputs.py")

        with mock.patch.object(module, "validate_generated_outputs", side_effect=FileNotFoundError("missing-python")):
            self.assertEqual(1, module.main())


if __name__ == "__main__":
    unittest.main()
