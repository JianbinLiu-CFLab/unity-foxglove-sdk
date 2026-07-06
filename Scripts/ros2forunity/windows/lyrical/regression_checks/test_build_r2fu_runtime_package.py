#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for runtime package builder path safety.

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[5]
BUILDER_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "lyrical" / "build_r2fu_runtime_package.py"


def load_builder_module():
    """Load the runtime package builder module under test."""
    spec = importlib.util.spec_from_file_location("build_r2fu_runtime_package", BUILDER_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class RuntimePackageExtractionTests(unittest.TestCase):
    """Regression coverage for runtime package archive extraction."""

    def setUp(self) -> None:
        """Load a fresh reference to the builder module for each test."""
        self.builder = load_builder_module()

    def test_extract_runtime_rejects_zip_slip_entries(self) -> None:
        """Reject archive entries that would escape the package root."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "runtime.zip"
            package = root / "package"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("Ros2ForUnity/../escape.txt", "nope")

            paths = self.builder.BuildPaths(archive, root / "inventory.json", package)

            with self.assertRaises(ValueError):
                self.builder.extract_runtime(paths)

            self.assertFalse((root / "escape.txt").exists())

    def test_extract_runtime_keeps_valid_entries_under_runtime_root(self) -> None:
        """Extract normal runtime archive entries beneath the package runtime root."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "runtime.zip"
            package = root / "package"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("Ros2ForUnity/Scripts/ROS2ForUnity.cs", "ok")

            paths = self.builder.BuildPaths(archive, root / "inventory.json", package)

            self.builder.extract_runtime(paths)

            target = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2ForUnity.cs"
            self.assertEqual("ok", target.read_text(encoding="utf-8"))

    def test_patch_ros2_for_unity_requires_copyright_replacement(self) -> None:
        """Patch generation fails when the expected copyright line is absent."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            source = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2ForUnity.cs"
            source.parent.mkdir(parents=True)
            source.write_text(
                '    private static string ros2ForUnityAssetFolderName = "Ros2ForUnity";\n'
                + self.builder.UPSTREAM_PATH_BLOCK,
                encoding="utf-8",
            )

            with self.assertRaises(ValueError):
                self.builder.patch_ros2_for_unity(package)

    def test_require_inputs_rejects_mismatched_artifact_size(self) -> None:
        """Reject inventory files whose optional artifact size disagrees."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            artifact = root / self.builder.ARTIFACT_NAME
            artifact.write_bytes(b"abc")
            inventory = root / "inventory.json"
            inventory.write_text(
                '{"runtimeId": "r2fu-lyrical-win64", "sha256": "'
                + self.builder.sha256_file(artifact)
                + '", "artifactSize": 99, "fileCount": 1}',
                encoding="utf-8",
            )
            license_file = root / "LICENSE.AL2"

            paths = self.builder.BuildPaths(artifact, inventory, root / "package")
            with mock.patch.object(self.builder, "UPSTREAM_LICENSE", license_file):
                license_file.write_text("license", encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.builder.require_inputs(paths)

    def test_require_inputs_names_missing_upstream_license(self) -> None:
        """Report the missing upstream package license before generation starts."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            artifact = root / self.builder.ARTIFACT_NAME
            artifact.write_bytes(b"abc")
            inventory = root / "inventory.json"
            inventory.write_text(
                '{"runtimeId": "r2fu-lyrical-win64", "sha256": "'
                + self.builder.sha256_file(artifact)
                + '", "artifactSize": 3, "fileCount": 1}',
                encoding="utf-8",
            )

            paths = self.builder.BuildPaths(artifact, inventory, root / "package")
            with mock.patch.object(self.builder, "UPSTREAM_LICENSE", root / "missing" / "LICENSE.AL2"):
                with self.assertRaisesRegex(FileNotFoundError, "Missing upstream ROS2 For Unity license"):
                    self.builder.require_inputs(paths)

    def test_patch_rmw_guard_replaces_multiline_validate_body(self) -> None:
        """RMW guard replacement handles an upstream multiline validation body."""
        source = (
            "    private static ConsoleCancelEventHandler consoleCancelHandler;\n"
            "    private static void ValidateRmwImplementation(string rmwImpl)\n"
            "    {\n"
            "        if (string.IsNullOrEmpty(rmwImpl))\n"
            "        {\n"
            "            return;\n"
            "        }\n"
            "    }\n\n"
            "    private static bool IsSupportedRmwImplementation(string rmwImpl)\n"
            "    {\n"
            "        return rmwImpl == \"rmw_fastrtps_cpp\";\n"
            "    }\n\n"
            "    private void Init()\n"
            "    {\n"
            "            string rmwImpl = Ros2cs.GetRMWImplementation();\n"
            "    }\n\n"
            "    private void RegisterCtrlCHandler()\n"
            "    {\n"
            "    }\n"
        )

        patched = self.builder.patch_rmw_guard(source)

        self.assertIn("ValidateRmwImplementation(rmwImpl);", patched)
        self.assertIn("supportedRmwImplementations", patched)
        self.assertNotIn("return rmwImpl == \"rmw_fastrtps_cpp\";", patched)

    def test_package_json_declares_empty_dependencies_for_self_contained_runtime(self) -> None:
        """Generated package metadata should explicitly document that the runtime package is self-contained."""
        package = self.builder.package_json()

        self.assertEqual({}, package.get("dependencies"))

    def test_standalone_isolation_keeps_windows_env_setup_single_pass(self) -> None:
        """Standalone env ownership should not be repeated in the Windows PATH block."""
        source = (
            ROOT
            / "Packages"
            / self.builder.PACKAGE_NAME
            / "Runtime"
            / "Ros2ForUnity"
            / "Scripts"
            / "ROS2ForUnity.cs"
        ).read_text(encoding="utf-8")

        patched = self.builder.patch_standalone_environment_isolation(source)
        constructor = patched[patched.find("internal ROS2ForUnity()") :]
        windows_start = constructor.find("if (GetOS() == Platform.Windows)")
        windows_end = constructor.find("} else {", windows_start)
        windows_block = constructor[windows_start:windows_end]

        self.assertIn("#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN", patched)
        self.assertIn("PrewarmUnityPaths", patched)
        self.assertNotIn("SetStandaloneRosDistro(currentRos2Version)", windows_block)
        self.assertNotIn("SetStandalonePrefixPath();", windows_block)
        self.assertNotIn("SetStandaloneRmwImplementation();", windows_block)
        self.assertNotIn("SetStandaloneRcutilsConsoleMode();", windows_block)

    def test_component_patch_prewarms_paths_and_removes_dead_shutdown_reset(self) -> None:
        """ROS2UnityComponent patch should prewarm Unity API paths and remove the dead reset."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            component = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2UnityComponent.cs"
            component.parent.mkdir(parents=True)
            component.write_text(
                "class ROS2UnityComponent\n"
                "{\n"
                "    private readonly object mutex = new object();\n"
                "    private double spinTimeout = 0.0001;\n\n"
                "    void LazyConstruct()\n"
                "    {\n"
                "            runtimeShutdownRequested = false;\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )

            self.builder.patch_component_main_thread_prewarm(package)

            patched = component.read_text(encoding="utf-8")
            self.assertIn("private void Awake()", patched)
            self.assertIn("ROS2ForUnity.PrewarmUnityPaths();", patched)
            self.assertNotIn("runtimeShutdownRequested = false;", patched)

    def test_zenoh_router_patch_documents_development_profile(self) -> None:
        """Generated router configs should retain trusted-lab security notes."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            config = (
                package
                / "Runtime"
                / "Ros2ForUnity"
                / "Plugins"
                / "Windows"
                / "x86_64"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"
            )
            mirror = (
                package
                / "Runtime"
                / "Ros2ForUnity"
                / "StreamingAssets"
                / "Ros2ForUnity"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"
            )
            config.parent.mkdir(parents=True)
            mirror.parent.mkdir(parents=True)
            text = (
                "/// This file attempts to list and document available configuration elements.\n"
                "/// For a more complete view of the configuration's structure, check out `zenoh/src/config.rs`'s `Config` structure.\n"
                "/// Note that the values here are correctly typed, but may not be sensible, so copying this file to change only the parts that matter to you is not good practice.\n"
                "{\n"
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together\n"
                "      accept_pending: 10000,\n"
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together\n"
                "      max_sessions: 10000,\n"
                "}\n"
            )
            config.write_text(text, encoding="utf-8")
            mirror.write_text(text, encoding="utf-8")

            self.builder.patch_zenoh_router_config_notes(package)

            patched = config.read_text(encoding="utf-8")
            self.assertIn("without authentication or ACLs", patched)
            self.assertIn("localhost-only or ACL-protected deployment profile", patched)
            self.assertIn("high development default is unsuitable", patched)
            self.assertEqual(patched, mirror.read_text(encoding="utf-8"))

    def test_standalone_isolation_rejects_partial_startup_patch(self) -> None:
        """Do not accept a source that declares metadata without standalone setup calls."""
        source = (
            "    public void CheckIntegrity()\n"
            "    {\n"
            "        string ros2SourcedCodename = GetROSVersionSourced();\n"
            "    }\n"
            "    private void Start()\n"
            "    {\n"
            "            // Load metadata\n"
            "            LoadMetadata();\n"
            "            string packagedRos2Version = GetMetadataValue(ros2csMetadata, \"/ros2cs/ros2\");\n"
            "            string standalone = IsStandalone() ? \"standalone\" : \"non-standalone\";\n"
            "            CheckIntegrity();\n"
            "                if (IsStandalone())\n"
            "    }\n"
        )

        with self.assertRaisesRegex(ValueError, "missing required setup calls"):
            self.builder.patch_standalone_environment_isolation(source)

    def test_patch_ros_time_source_contract_updates_dotnet_copyright(self) -> None:
        """Patch DotnetTimeSource copyright alongside bool-returning time contracts."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            time_dir = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Time"
            time_dir.mkdir(parents=True)
            (time_dir / "DotnetTimeSource.cs").write_text(
                "// Modifications Copyright (c) 2026 Jianbin Liu.\n",
                encoding="utf-8",
            )
            for name in ("ROS2TimeSource.cs", "ROS2ScalableTimeSource.cs"):
                (time_dir / name).write_text(
                    "  public bool GetTime(out int seconds, out uint nanoseconds)\n"
                    "  {\n"
                    "    // U2F-LOCAL-PATCH: match newer ros2cs bool-returning ITimeSource contract.\n"
                    "    seconds = 0;\n"
                    "    nanoseconds = 0;\n"
                    "    if (seconds == 0) return false;\n"
                    "    return true;\n"
                    "  }\n",
                    encoding="utf-8",
                )

            self.builder.patch_ros_time_source_contract(package)

            self.assertIn(
                "Unity2Foxglove contributors",
                (time_dir / "DotnetTimeSource.cs").read_text(encoding="utf-8"),
            )

    def test_build_package_restores_existing_package_when_generation_fails(self) -> None:
        """A failed regeneration should not leave the package directory destroyed."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Packages" / self.builder.PACKAGE_NAME
            package.mkdir(parents=True)
            sentinel = package / "sentinel.txt"
            sentinel.write_text("keep", encoding="utf-8")
            paths = self.builder.BuildPaths(root / "runtime.zip", root / "inventory.json", package)
            artifact = self.builder.RuntimeArtifact(
                name=self.builder.ARTIFACT_NAME,
                sha256="0" * 64,
                size=1,
                inventory_file_count=1,
            )

            with mock.patch.object(self.builder, "ROOT", root):
                with mock.patch.object(self.builder, "require_inputs", return_value=({}, artifact)):
                    with mock.patch.object(self.builder, "extract_runtime", side_effect=RuntimeError("boom")):
                        with self.assertRaises(RuntimeError):
                            self.builder.build_package(paths)

            self.assertTrue(sentinel.exists())
            self.assertEqual("keep", sentinel.read_text(encoding="utf-8"))

    def test_build_package_keeps_existing_package_if_reset_fails(self) -> None:
        """Rollback should not mask reset_package_dir path-safety failures."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "not-the-package"
            package.mkdir()
            sentinel = package / "sentinel.txt"
            sentinel.write_text("keep", encoding="utf-8")
            paths = self.builder.BuildPaths(root / "runtime.zip", root / "inventory.json", package)
            artifact = self.builder.RuntimeArtifact(
                name=self.builder.ARTIFACT_NAME,
                sha256="0" * 64,
                size=1,
                inventory_file_count=1,
            )

            with mock.patch.object(self.builder, "ROOT", root):
                with mock.patch.object(self.builder, "require_inputs", return_value=({}, artifact)):
                    with self.assertRaises(ValueError):
                        self.builder.build_package(paths)

            self.assertTrue(sentinel.exists())
            self.assertEqual("keep", sentinel.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
