#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for runtime package builder path safety.

from __future__ import annotations

import importlib.util
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
BUILDER_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "humble" / "build_r2fu_runtime_package.py"


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
        """Humble must prevent Unity from installing a second distro runtime."""
        manifest = self.builder.package_json()

        self.assertEqual(
            [
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
            ],
            manifest["unity2foxgloveConflicts"],
        )
        artifact = self.builder.RuntimeArtifact("artifact.zip", "0" * 64, 1, 1)
        self.assertIn(
            "The script assembly is intentionally named `Unity2Foxglove.Ros2ForUnity.Runtime`",
            self.builder.readme_text(artifact),
        )
        self.assertIn("process-wide ROS environment", self.builder.readme_text(artifact))
        self.assertIn("restart Unity after changing the runtime package", self.builder.readme_text(artifact))
        self.assertIn("RMW_IMPLEMENTATION", self.builder.readme_text(artifact))
        self.assertIn("ROS_DISTRO", self.builder.readme_text(artifact))

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

    def test_normalize_ros2cs_plugin_roots_rewrites_both_package_copies(self) -> None:
        """Generated metadata must not retain the artifact producer's absolute plugin path."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            metadata_files = (
                package / "Runtime" / "Ros2ForUnity" / "Plugins" / "metadata_ros2cs.xml",
                package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64" / "metadata_ros2cs.xml",
            )
            for path in metadata_files:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(
                    '<ros2cs><plugins root="D:\\producer\\plugins" runtime_file_count="1"><file>a.dll</file></plugins></ros2cs>',
                    encoding="utf-8",
                )

            self.builder.normalize_ros2cs_plugin_roots(package)

            for path in metadata_files:
                text = path.read_text(encoding="utf-8")
                self.assertIn('<plugins root="." runtime_file_count="1">', text)
                self.assertIn("<file>a.dll</file>", text)
                self.assertNotIn("D:\\producer", text)

    def test_legacy_managed_dll_meta_preserves_guid_while_upgrading_importer(self) -> None:
        """Managed DLL metadata must use the same Windows PluginImporter contract as native DLLs."""
        legacy = b"fileFormatVersion: 2\nguid: abcdefabcdefabcdefabcdefabcdefab\n"

        data = self.builder.normalize_meta_overlay(
            "Runtime/Ros2ForUnity/Plugins/ros2cs_core.dll.meta",
            legacy,
        ).decode("utf-8")

        self.assertIn("guid: abcdefabcdefabcdefabcdefabcdefab", data)
        self.assertIn("PluginImporter:", data)
        self.assertIn("Standalone: Windows", data)

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
            inventory = support / "r2fu-humble-win64-runtime-inventory.json"
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

    def test_player_metadata_patch_uses_streaming_assets_root(self) -> None:
        """Player metadata must follow the staged StreamingAssets layout."""
        source = 'string ros2csMetadataPath = GetPluginPath() + separator + "metadata_ros2cs.xml";'
        patched = self.builder.patch_runtime_metadata_path(source)
        self.assertIn('GetRos2ForUnityPath() + separator + "metadata_ros2cs.xml"', patched)
        self.assertNotIn("GetPluginPath()", patched)

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
        patched_startup = self.builder.patch_standalone_environment_isolation(
            "            // Load metadata\n"
            "            LoadMetadata();\n"
            "            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();\n"
        )

        self.assertIn("AssemblyReloadEvents.beforeAssemblyReload += ShutdownShared", patched_runtime)
        self.assertIn("AssemblyReloadEvents.beforeAssemblyReload -= ShutdownShared", patched_runtime)
        self.assertNotIn("ThrowIfUninitialized", patched_runtime)
        self.assertIn("LoadMetadata() must complete before metadata-backed properties are read.", patched_runtime)
        self.assertIn("must be constructed on the Unity main thread", patched_time)
        self.assertEqual(1, patched_startup.count("sourcedRosDistroBeforeStandalonePatch"))

    def test_standalone_prefix_patch_removes_unused_prefix_source(self) -> None:
        """Standalone isolation must not leave the removed AMENT log source behind."""
        source = '''        string prefixPath = GetRos2ForUnityPath();
        string prefixSource = "asset root";
        string streamingAssetsPrefixPath = Path.Combine(Application.streamingAssetsPath, ros2ForUnityAssetFolderName);
        string pluginPrefixPath = GetPluginPath();
        if (Directory.Exists(Path.Combine(streamingAssetsPrefixPath, "share")))
        {
            prefixPath = streamingAssetsPrefixPath;
            prefixSource = "StreamingAssets";
        }
        else if (Directory.Exists(Path.Combine(pluginPrefixPath, "share")))
        {
            prefixPath = pluginPrefixPath;
            prefixSource = "plugin directory";
        }
        string currentPrefixPath = Environment.GetEnvironmentVariable("AMENT_PREFIX_PATH");
        char envPathSep = GetOS() == Platform.Windows ? ';' : ':';

        if (String.IsNullOrEmpty(currentPrefixPath))
        {
            SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
            Debug.Log("AMENT_PREFIX_PATH set to: " + prefixPath + " (source: " + prefixSource + ")");
            return;
        }

        StringComparison comparison = GetOS() == Platform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (string entry in currentPrefixPath.Split(envPathSep))
        {
            if (String.Equals(entry.Trim(), prefixPath, comparison))
            {
                Debug.Log("AMENT_PREFIX_PATH already contains: " + prefixPath + " (source: " + prefixSource + ")");
                return;
            }
        }

        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath + envPathSep + currentPrefixPath);
        Debug.Log("AMENT_PREFIX_PATH prepended with: " + prefixPath + " (source: " + prefixSource + ")");
'''

        patched = self.builder.patch_standalone_environment_isolation(source)

        self.assertNotIn("prefixSource", patched)
        self.assertIn('SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);', patched)

    def test_require_inputs_rejects_mismatched_artifact_size(self) -> None:
        """Reject inventory files whose optional artifact size disagrees."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            artifact = root / self.builder.ARTIFACT_NAME
            artifact.write_bytes(b"abc")
            inventory = root / "inventory.json"
            inventory.write_text(
                '{"runtimeId": "r2fu-humble-win64", "sha256": "'
                + self.builder.sha256_file(artifact)
                + '", "artifactSize": 99, "fileCount": 1}',
                encoding="utf-8",
            )
            paths = self.builder.BuildPaths(artifact, inventory, root / "package")

            with mock.patch.object(self.builder, "UPSTREAM_LICENSE", root / "LICENSE.AL2"):
                self.builder.UPSTREAM_LICENSE.write_text("license", encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.builder.require_inputs(paths)

    def test_collect_local_patch_overlays_rejects_invalid_utf8(self) -> None:
        """Overlay capture should fail loudly on corrupt UTF-8."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            script = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2UnityComponent.cs"
            script.parent.mkdir(parents=True)
            script.write_bytes(b"\xff")

            with self.assertRaises(UnicodeDecodeError):
                self.builder.collect_local_patch_overlays(package)

    def test_default_inventory_is_kept_outside_resettable_package(self) -> None:
        """The default inventory must live in the adapter compliance directory."""
        expected = (
            self.builder.ROOT
            / "Packages"
            / "dev.unity2foxglove.ros2forunity"
            / "Compliance"
            / (self.builder.RUNTIME_ID + "-runtime-inventory.json")
        )
        self.assertEqual(expected.resolve(), self.builder.DEFAULT_INVENTORY.resolve())

    def test_supplemental_ament_index_files_restore_only_missing_entries(self) -> None:
        """Legacy ament-index files survive archive extraction without overwriting new entries."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            root = package.joinpath(*self.builder.SUPPLEMENTAL_RUNTIME_RELATIVE.split("/"))
            present = root / "resource_index" / "packages" / "present"
            missing = root / "resource_index" / "packages" / "missing"
            present.parent.mkdir(parents=True)
            present.write_bytes(b"legacy-present")
            missing.write_bytes(b"legacy-missing")

            overlays = self.builder.collect_supplemental_runtime_files(package)
            present.write_bytes(b"artifact-present")
            missing.unlink()
            self.builder.apply_supplemental_runtime_files(package, overlays)

            self.assertEqual(b"artifact-present", present.read_bytes())
            self.assertEqual(b"legacy-missing", missing.read_bytes())

    def test_sensor_overlay_survives_runtime_regeneration(self) -> None:
        """The patched Sensor source must be restored after an upstream refresh."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            sensor = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Sensor.cs"
            sensor.parent.mkdir(parents=True)
            sensor.write_text("patched sensor\n", encoding="utf-8")

            overlays = self.builder.collect_local_patch_overlays(package)
            relative = "Runtime/Ros2ForUnity/Scripts/Sensor.cs"
            self.assertEqual("patched sensor\n", overlays[relative])

            sensor.write_text("upstream sensor\n", encoding="utf-8")
            self.builder.apply_local_patch_overlays(package, overlays)
            self.assertEqual("patched sensor\n", sensor.read_text(encoding="utf-8"))

    def test_ros2_for_unity_overlay_survives_runtime_regeneration(self) -> None:
        """The lifecycle/environment patch must be restored after an upstream refresh."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            source = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2ForUnity.cs"
            source.parent.mkdir(parents=True)
            source.write_text("process environment lease marker\n", encoding="utf-8")

            overlays = self.builder.collect_local_patch_overlays(package)
            relative = "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs"
            self.assertEqual("process environment lease marker\n", overlays[relative])

            source.write_text("upstream source\n", encoding="utf-8")
            self.builder.apply_local_patch_overlays(package, overlays)
            self.assertEqual("process environment lease marker\n", source.read_text(encoding="utf-8"))

    def test_ros2_for_unity_overlay_preserves_native_environment_helper(self) -> None:
        """A refreshed Humble package must retain the UCRT environment helper."""
        overlays = self.builder.collect_local_patch_overlays(self.builder.DEFAULT_PACKAGE)
        relative = "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs"
        source = overlays[relative]

        self.assertIn(relative, self.builder.LOCAL_PATCH_OVERLAY_FILES)
        self.assertIn("_wputenv_s(name, value ?? String.Empty)", source)
        self.assertIn(
            "[DllImport(\"ucrtbase.dll\", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]",
            source,
        )

    def test_existing_package_path_patch_still_applies_rmw_guard(self) -> None:
        """The early package-path patch branch must not skip RMW validation."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            source = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2ForUnity.cs"
            source.parent.mkdir(parents=True)
            source.write_text(
                "// Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.\n"
                "Unity2Foxglove package path support\n"
                "    private static ConsoleCancelEventHandler consoleCancelHandler;\n"
                "    private static void ValidateRmwImplementation(string rmwImpl)\n"
                "    {\n"
                "        return;\n"
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
                "standalone runtime must not inherit a sourced ROS2 workspace\n"
                "standalone runtime owns its RMW selection\n"
                "selectedRmwImplementation\n"
                "standalone runtime owns ROS_DISTRO\n"
                "WarnIfStandaloneRosDistroOverride\n"
                "sourcedRosDistroBeforeStandalonePatch\n"
                "CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch)\n",
                encoding="utf-8",
            )

            with mock.patch.object(self.builder, "patch_ros2cs_logger_callback_api", side_effect=lambda text: text):
                with mock.patch.object(self.builder, "patch_standalone_environment_isolation", side_effect=lambda text: text):
                    self.builder.patch_ros2_for_unity(package)

            patched = source.read_text(encoding="utf-8")
            self.assertIn("ValidateRmwImplementation(rmwImpl);", patched)
            self.assertIn("expectedRmwImplementation", patched)

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
                            _ = self.builder.build_package(paths)

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
            paths = self.builder.BuildPaths(root / "runtime.zip", root / "inventory.json", package)
            artifact = self.builder.RuntimeArtifact(
                name=self.builder.ARTIFACT_NAME,
                sha256="0" * 64,
                size=1,
                inventory_file_count=1,
            )
            real_copytree = self.builder.shutil.copytree
            copy_count = 0

            def fail_restore_copy(source, destination, *args, **kwargs):
                """Fail the restore copy after allowing the backup copy."""
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
                        _ = self.builder.build_package(paths)

    def test_build_package_returns_artifact_identity(self) -> None:
        """main can print the already-computed hash without hashing the zip again."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Packages" / self.builder.PACKAGE_NAME
            paths = self.builder.BuildPaths(root / "runtime.zip", root / "inventory.json", package)
            artifact = self.builder.RuntimeArtifact(
                name=self.builder.ARTIFACT_NAME,
                sha256="1" * 64,
                size=1,
                inventory_file_count=1,
            )

            with mock.patch.object(self.builder, "ROOT", root):
                with mock.patch.object(self.builder, "require_inputs", return_value=({}, artifact)):
                    with mock.patch.object(self.builder, "extract_runtime", return_value=None):
                        with mock.patch.object(self.builder, "normalize_ros2cs_plugin_roots", return_value=None):
                            with mock.patch.object(self.builder, "prune_non_contract_examples", return_value=None):
                                with mock.patch.object(self.builder, "patch_ros2_for_unity", return_value=None):
                                    with mock.patch.object(self.builder, "apply_local_patch_overlays", return_value=None):
                                        with mock.patch.object(self.builder, "patch_ros_time_source_contract", return_value=None):
                                            with mock.patch.object(self.builder, "write_package_files", return_value=None):
                                                with mock.patch.object(self.builder, "apply_meta_overlays", return_value=None):
                                                    with mock.patch.object(self.builder, "write_generated_metas", return_value=None):
                                                        returned = self.builder.build_package(paths)

            self.assertIs(artifact, returned)


if __name__ == "__main__":
    unittest.main()
