from __future__ import annotations

from .foundation import *


class RuntimeOverlayInventoryTests:
    """Exercise local overlays and inventory safety checks."""

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


__all__ = ["RuntimeOverlayInventoryTests"]
