#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for runtime package builder path safety.

from __future__ import annotations

import importlib.util
import hashlib
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[5]
assert (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "package.json").exists(), (
    f"Repo root resolution failed: {ROOT}"
)
BUILDER_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "jazzy" / "build_r2fu_runtime_package.py"


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

    def test_package_manifest_declares_other_runtime_packages_as_conflicts(self) -> None:
        """Jazzy must prevent Unity from installing a second distro runtime."""
        manifest = self.builder.package_json()

        self.assertEqual(
            [
                "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
                "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
            ],
            manifest["unity2foxgloveConflicts"],
        )
        artifact = self.builder.RuntimeArtifact("artifact.zip", "0" * 64, 1, 1)
        self.assertIn(
            "The script assembly is intentionally named `Unity2Foxglove.Ros2ForUnity.Runtime`",
            self.builder.readme_text(artifact),
        )

    def test_extract_runtime_rejects_zip_slip_entries(self) -> None:
        """Reject archive entries that would escape the package root."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "runtime.zip"
            package = root / "package"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("Ros2ForUnity/../escape.txt", "nope")

            paths = self.builder.BuildPaths(archive, root / "inventory.json", package, root / "ros2-bin")

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

            paths = self.builder.BuildPaths(archive, root / "inventory.json", package, root / "ros2-bin")

            self.builder.extract_runtime(paths)

            target = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2ForUnity.cs"
            self.assertEqual("ok", target.read_text(encoding="utf-8"))

    def test_patch_deps_json_sha512_updates_inventory_hash(self) -> None:
        """Generated deps.json files should carry integrity hints and matching inventory hashes."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            plugin_root = package / "Runtime" / "Ros2ForUnity" / "Plugins"
            support = package / "RuntimeSupport"
            plugin_root.mkdir(parents=True)
            support.mkdir(parents=True)
            (plugin_root / "example.dll").write_bytes(b"example")
            (plugin_root / "dependency.dll").write_bytes(b"dependency")
            deps = plugin_root / "example.deps.json"
            deps.write_text(
                json.dumps({"libraries": {"example/1.0.0": {"sha512": ""}, "dependency/0.0.0": {"sha512": ""}}}),
                encoding="utf-8",
            )
            inventory = support / "r2fu-jazzy-win64-runtime-inventory.json"
            inventory.write_text(
                json.dumps({"files": [{"path": "Ros2ForUnity/Plugins/example.deps.json", "sha256": "", "size": 0}]}),
                encoding="utf-8",
            )

            self.builder.patch_deps_json_sha512(package)

            patched = json.loads(deps.read_text(encoding="utf-8"))
            self.assertEqual(128, len(patched["libraries"]["example/1.0.0"]["sha512"]))
            self.assertEqual(128, len(patched["libraries"]["dependency/0.0.0"]["sha512"]))
            patched_inventory = json.loads(inventory.read_text(encoding="utf-8"))
            self.assertEqual(self.builder.sha256_file(deps), patched_inventory["files"][0]["sha256"])
            self.assertEqual(deps.stat().st_size, patched_inventory["files"][0]["size"])

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

    def test_runtime_safety_patches_survive_the_new_upstream_layout(self) -> None:
        """Lifecycle and Unity-time safety patches must survive an upstream runtime refresh."""
        runtime = (
            "        EditorApplication.playModeStateChanged += EditorPlayStateChanged;\n"
            "        EditorApplication.quitting += ShutdownShared;\n"
            "        editorHandlersRegistered = true;\n"
            "        EditorApplication.playModeStateChanged -= EditorPlayStateChanged;\n"
            "        EditorApplication.quitting -= ShutdownShared;\n"
            "        editorHandlersRegistered = false;\n"
            "    }\n\n"
            "    private static void ThrowIfUninitialized(string callContext)\n"
            "    {\n"
            "        if (!isInitialized)\n"
            "        {\n"
            "            throw new InvalidOperationException(\"not initialized\");\n"
            "        }\n"
            "    }\n\n"
            "            throw new InvalidOperationException(\"Metadata document is empty while reading \" + valuePath);\n"
        )
        unity_time = (
            "  public UnityTimeSource()\n"
            "  {\n"
            "    mainThreadId = Thread.CurrentThread.ManagedThreadId;\n"
            "    lastReadingSecs = Time.timeAsDouble;\n"
            "  }\n"
        )

        patched_runtime = self.builder.patch_runtime_lifecycle_safety(runtime)
        patched_time = self.builder.patch_unity_time_source_main_thread_guard(unity_time)

        self.assertIn("AssemblyReloadEvents.beforeAssemblyReload += ShutdownShared", patched_runtime)
        self.assertIn("AssemblyReloadEvents.beforeAssemblyReload -= ShutdownShared", patched_runtime)
        self.assertNotIn("ThrowIfUninitialized", patched_runtime)
        self.assertIn("LoadMetadata() must complete before metadata-backed properties are read.", patched_runtime)
        self.assertIn("must be constructed on the Unity main thread", patched_time)

    def test_build_package_restores_existing_package_when_generation_fails(self) -> None:
        """A failed regeneration should not leave the package directory destroyed."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Packages" / self.builder.PACKAGE_NAME
            package.mkdir(parents=True)
            sentinel = package / "sentinel.txt"
            sentinel.write_text("keep", encoding="utf-8")
            paths = self.builder.BuildPaths(root / "runtime.zip", root / "inventory.json", package, root / "ros2-bin")
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
            rollback_root = root / "build" / "r2fu-runtime-package-rollback"
            self.assertEqual([], list(rollback_root.iterdir()))

    def test_build_package_preserves_snapshot_when_rollback_fails(self) -> None:
        """A failed rollback must retain the only recoverable copy of the old package."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Packages" / self.builder.PACKAGE_NAME
            package.mkdir(parents=True)
            (package / "sentinel.txt").write_text("keep", encoding="utf-8")
            paths = self.builder.BuildPaths(
                root / "runtime.zip",
                root / "inventory.json",
                package,
                root / "ros2-bin",
            )
            artifact = self.builder.RuntimeArtifact(
                name=self.builder.ARTIFACT_NAME,
                sha256="0" * 64,
                size=1,
                inventory_file_count=1,
            )
            real_copytree = self.builder.shutil.copytree
            copy_count = 0

            def fail_restore_copy(source, destination, *args, **kwargs):
                nonlocal copy_count
                copy_count += 1
                if copy_count == 2:
                    raise OSError("restore copy failed")
                return real_copytree(source, destination, *args, **kwargs)

            with mock.patch.object(self.builder, "ROOT", root):
                with mock.patch.object(self.builder, "require_inputs", return_value=({}, artifact)):
                    with mock.patch.object(self.builder, "extract_runtime", side_effect=RuntimeError("generation failed")):
                        with mock.patch.object(self.builder.shutil, "copytree", side_effect=fail_restore_copy):
                            with self.assertRaises(RuntimeError) as raised:
                                _ = self.builder.build_package(paths)

            rollback_root = root / "build" / "r2fu-runtime-package-rollback"
            snapshots = list(rollback_root.iterdir())
            self.assertEqual(1, len(snapshots), f"expected one preserved snapshot, got {snapshots}")
            snapshot = snapshots[0]
            self.assertEqual("keep", (snapshot / "sentinel.txt").read_text(encoding="utf-8"))
            self.assertIn("generation failed", str(raised.exception))
            self.assertIn("restore copy failed", str(raised.exception))
            self.assertIn(str(snapshot), str(raised.exception))

    def test_build_package_keeps_existing_package_if_reset_fails(self) -> None:
        """Rollback should not mask reset_package_dir path-safety failures."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "not-the-package"
            package.mkdir()
            sentinel = package / "sentinel.txt"
            sentinel.write_text("keep", encoding="utf-8")
            paths = self.builder.BuildPaths(root / "runtime.zip", root / "inventory.json", package, root / "ros2-bin")
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

    def test_generated_dll_meta_uses_plugin_importer(self) -> None:
        """Generated Unity DLL metadata should import native plugins, not text."""
        relative = "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rcl.dll"

        text = self.builder.generated_meta_text(Path(relative), relative, is_dir=False, guid="1" * 32)

        self.assertIn("PluginImporter:", text)
        self.assertIn("Standalone: Windows", text)
        self.assertIn("CPU: x86_64", text)
        self.assertNotIn("TextScriptImporter:", text)

    def test_legacy_dll_meta_overlay_preserves_guid_while_upgrading_importer(self) -> None:
        """Legacy two-line DLL metas should keep GUIDs while gaining PluginImporter."""
        legacy = b"fileFormatVersion: 2\nguid: abcdefabcdefabcdefabcdefabcdefab\n"

        data = self.builder.normalize_meta_overlay(
            "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rcl.dll.meta",
            legacy,
        ).decode("utf-8")

        self.assertIn("guid: abcdefabcdefabcdefabcdefabcdefab", data)
        self.assertIn("PluginImporter:", data)
        self.assertIn("Standalone: Windows", data)

    def test_copy_supplemental_runtime_dlls_uses_supplied_ros2_bin(self) -> None:
        """Supplemental DLLs should come from the caller-selected ROS2 bin."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "package"
            plugin_root = package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
            plugin_root.mkdir(parents=True)
            ros2_bin = root / "custom-ros2-bin"
            ros2_bin.mkdir()
            for name in self.builder.PHASE161_SUPPLEMENTAL_RUNTIME_DLLS:
                (ros2_bin / name).write_bytes(b"custom")

            self.builder.copy_supplemental_runtime_dlls(package, ros2_bin)

            for name in self.builder.PHASE161_SUPPLEMENTAL_RUNTIME_DLLS:
                self.assertEqual(b"custom", (plugin_root / name).read_bytes())

    def test_patch_standalone_environment_rejects_unpatched_path_call_site(self) -> None:
        """Bootstrap patching should fail if the managed PATH write call remains."""
        text = (
            "using System.Reflection;\n"
            "public class ROS2ForUnity\n"
            "{\n"
            "    private bool ownsLifecycle;\n"
            "    private string GetEnvPathVariableValue()\n"
            "    {\n"
            "        return Environment.GetEnvironmentVariable(GetEnvPathVariableName());\n"
            "    }\n"
            "        Environment.SetEnvironmentVariable(GetEnvPathVariableName(), string.Join(envPathSep.ToString(), entries));\n"
            "        Environment.SetEnvironmentVariable(GetEnvPathVariableName(), string.Join(envPathSep.ToString(), entries));\n"
            "}\n"
        )

        with self.assertRaises(ValueError):
            self.builder.patch_standalone_environment_bootstrap(text)

    def test_require_inputs_rejects_mismatched_artifact_size(self) -> None:
        """Inventory artifactSize must match the artifact when it is present."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            artifact = root / self.builder.ARTIFACT_NAME
            artifact.write_bytes(b"artifact")
            digest = hashlib.sha256(b"artifact").hexdigest()
            inventory = root / "inventory.json"
            inventory.write_text(
                json.dumps(
                    {
                        "runtimeId": self.builder.RUNTIME_ID,
                        "sha256": digest,
                        "artifactSize": 999,
                        "fileCount": 1,
                    }
                ),
                encoding="utf-8",
            )
            paths = self.builder.BuildPaths(artifact, inventory, root / "package", root / "ros2-bin")

            with mock.patch.object(self.builder, "EXPECTED_ARTIFACT_SHA256", digest):
                with self.assertRaises(ValueError):
                    self.builder.require_inputs(paths)


if __name__ == "__main__":
    unittest.main()
