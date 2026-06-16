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
        self.assertIn("writer.WriteByteArray(message.Data == null ? ReadOnlySpan<byte>.Empty : message.Data.Span);", generated)

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


if __name__ == "__main__":
    unittest.main()
