#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for generator/build/performance helper edge cases.

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]


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


class GeneratorBuildPerformanceScriptTests(unittest.TestCase):
    """Regression coverage for Phase 140-33 tooling findings."""

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

    def test_full_demo_scene_sanitizes_portable_fields_with_variable_indentation(self) -> None:
        """Sample sync should sanitize local-only fields even if Unity changes indentation."""
        module = load_module("sync_full_demo_under_test", "Scripts/samples/sync_full_demo.py")

        with tempfile.TemporaryDirectory() as temp:
            scene = Path(temp) / "scene.unity"
            scene.write_text(
                "FoxgloveManager:\n"
                "    _sharedToken: secret-token\n"
                "    _replayFilePath: C:/Users/Alice/private.mcap\n",
                encoding="utf-8",
            )

            payload = module.portable_full_demo_scene_payload(scene).decode("utf-8")

        self.assertIn("    _sharedToken:", payload)
        self.assertIn("    _replayFilePath:", payload)
        self.assertNotIn("secret-token", payload)
        self.assertNotIn("C:/Users/Alice", payload)

    def test_full_demo_scene_validation_rejects_local_paths_and_tokens(self) -> None:
        """Portable scene payload validation should fail loudly on local-only data."""
        module = load_module("sync_full_demo_validate_under_test", "Scripts/samples/sync_full_demo.py")

        with self.assertRaises(ValueError):
            module.validate_portable_full_demo_scene_payload(
                b"  _sharedToken: secret\n  _certificatePfxPath: C:/Users/Alice/cert.pfx\n"
            )

    def test_ros2_sample_default_imported_root_uses_package_manifest_version(self) -> None:
        """Sample sync should not hardcode the imported sample package version."""
        module = load_module("sync_ros2_samples_under_test", "Scripts/samples/sync_ros2_samples.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            manifest = root / "Packages" / "dev.unity2foxglove.ros2forunity" / "package.json"
            manifest.parent.mkdir(parents=True)
            manifest.write_text(json.dumps({"version": "9.8.7-preview.6"}), encoding="utf-8")

            imported_root = module.default_imported_root(root)

        self.assertTrue(imported_root.as_posix().endswith("Unity2Foxglove ROS2 For Unity/9.8.7-preview.6"))

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

    def test_performance_runner_reports_malformed_result_json_cleanly(self) -> None:
        """Malformed performance output should return failure without traceback noise."""
        module = load_module("performance_runner_under_test", "Scripts/performance/run_baseline.py")

        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp)
            (output / "phase35_performance_999.json").write_text("{not-json", encoding="utf-8")
            argv = ["run_baseline.py", "--quick", "--output", str(output), "--timeout-minutes", "0"]
            stdout = io.StringIO()
            completed = subprocess.CompletedProcess(args=["dotnet"], returncode=0)
            with mock.patch.object(module.sys, "argv", argv):
                with mock.patch.object(module, "_free_disk_bytes", return_value=10 * module.BYTES_PER_GIB):
                    with mock.patch.object(module, "_setup_nuget_cache", return_value={}):
                        with mock.patch.object(module.subprocess, "run", return_value=completed):
                            with contextlib.redirect_stdout(stdout):
                                result = module.main()

        self.assertEqual(module.EXIT_FAILURE, result)
        self.assertIn("malformed result JSON", stdout.getvalue())

    def test_asmdef_cycle_detection_handles_deep_graphs_without_recursion_error(self) -> None:
        """Architecture coupling analysis should tolerate deep acyclic graphs."""
        module = load_module("analyze_coupling_under_test", "Scripts/architecture/analyze_coupling.py")
        metrics = [
            module.AsmdefMetric(path=f"{index}.asmdef", name=f"A{index}", references=[f"A{index + 1}"])
            for index in range(1100)
        ]
        metrics.append(module.AsmdefMetric(path="1100.asmdef", name="A1100", references=[]))

        cycles = module.find_asmdef_cycles(metrics)

        self.assertEqual([], cycles)

    def test_registry_default_test_parse_warns_when_registry_shape_is_unrecognized(self) -> None:
        """A registry parse miss should be visible rather than disabling boundary checks."""
        module = load_module("analyze_coupling_registry_under_test", "Scripts/architecture/analyze_coupling.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            registry = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True)
            registry.write_text("DefaultValidation(typeof(Phase1Validation));\n", encoding="utf-8")
            stderr = io.StringIO()
            with contextlib.redirect_stderr(stderr):
                files = module.find_registry_default_test_files(root)

        self.assertEqual(set(), files)
        self.assertIn("warning", stderr.getvalue().lower())

    def test_draco_native_documents_inverse_compression_speed_mapping(self) -> None:
        """Native Draco speed constants should document their inverse CLI mapping."""
        source = (ROOT / "Scripts/native/draco_native/Unity2FoxgloveDracoNative.cpp").read_text(encoding="utf-8")

        self.assertIn("Draco speed option 3 corresponds to CLI compression level 7", source)


if __name__ == "__main__":
    unittest.main()
