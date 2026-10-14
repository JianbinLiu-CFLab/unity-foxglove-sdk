from __future__ import annotations

from .foundation import *


class RuntimeArchiveExtractionTests:
    """Exercise runtime archive extraction and package metadata."""

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


__all__ = ["RuntimeArchiveExtractionTests"]
