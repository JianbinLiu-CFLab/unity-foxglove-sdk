from __future__ import annotations

from .foundation import *


class LyricalRuntimeOverlayTests:
    """Exercise runtime overlays, safety patches, and package inputs"""

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

    def test_standalone_patch_normalizes_legacy_artifact_methods(self) -> None:
        """Legacy standalone method bodies must be replaced before isolation checks run."""
        source = (
            "    private static void SetStandalonePrefixPath()\n"
            "    {\n"
            "        string prefixSource = \"asset root\";\n"
            "        SetProcessEnvironmentVariable(\"AMENT_PREFIX_PATH\", prefixPath);\n"
            "    }\n\n"
            "    private static void SetStandaloneRmwImplementation()\n"
            "    {\n"
            "        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable(\"RMW_IMPLEMENTATION\")))\n"
            "        {\n"
            "            // Fast-RTPS is the bundled standalone RMW.\n"
            "            SetProcessEnvironmentVariable(\"RMW_IMPLEMENTATION\", \"rmw_fastrtps_cpp\");\n"
            "        }\n"
            "    }\n\n"
            "    private static void SetStandaloneRosDistro(string ros2Codename)\n"
            "    {\n"
            "        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable(\"ROS_DISTRO\")))\n"
            "        {\n"
            "            SetProcessEnvironmentVariable(\"ROS_DISTRO\", ros2Codename);\n"
            "        }\n"
            "    }\n\n"
            "    private static void SetStandaloneRos2csSpinFallback(string ros2Codename)\n"
            "    {\n"
            "    }\n"
        )

        patched = self.builder.replace_existing_standalone_prefix_method(source)
        patched = self.builder.replace_existing_standalone_rmw_method(patched)
        patched = self.builder.replace_existing_standalone_distro_method(patched)
        self.assertIn("standalone runtime must not inherit a sourced ROS2 workspace", patched)
        self.assertIn("standalone runtime owns its RMW selection while allowing Lyrical Zenoh", patched)
        self.assertIn("standalone runtime owns ROS_DISTRO", patched)
        self.assertNotIn("prefixSource", patched)
        self.assertNotIn("Fast-RTPS is the bundled standalone RMW", patched)
        self.assertEqual(
            patched,
            self.builder.replace_existing_standalone_distro_method(
                self.builder.replace_existing_standalone_rmw_method(
                    self.builder.replace_existing_standalone_prefix_method(patched)
                )
            ),
        )

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
            "            SetStandalonePrefixPath();\n"
            "            SetStandaloneRmwImplementation();\n"
        )

        self.assertIn("AssemblyReloadEvents.beforeAssemblyReload += ShutdownShared", patched_runtime)
        self.assertIn("AssemblyReloadEvents.beforeAssemblyReload -= ShutdownShared", patched_runtime)
        self.assertNotIn("ThrowIfUninitialized", patched_runtime)
        self.assertIn("LoadMetadata() must complete before metadata-backed properties are read.", patched_runtime)
        self.assertIn("must be constructed on the Unity main thread", patched_time)
        self.assertEqual(1, patched_startup.count("sourcedRosDistroBeforeStandalonePatch"))

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


__all__ = ["LyricalRuntimeOverlayTests"]
