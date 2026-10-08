from __future__ import annotations

from .foundation import *


class LyricalDependencyDescriptionTests:
    """Exercise dependency hash and distro-description contracts"""

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
            inventory = support / "r2fu-lyrical-win64-runtime-inventory.json"
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

    def test_patch_deps_json_sha512_removes_known_spurious_service_message_refs(self) -> None:
        """Known non-service message assemblies must not retain service_msgs references."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            plugin_root = package / "Runtime" / "Ros2ForUnity" / "Plugins"
            support = package / "RuntimeSupport"
            plugin_root.mkdir(parents=True)
            support.mkdir(parents=True)
            deps = plugin_root / "stereo_msgs_assembly.deps.json"
            deps.write_text(
                json.dumps(
                    {
                        "targets": {
                            ".NETStandard,Version=v2.0/": {
                                "stereo_msgs_assembly/1.0.0": {
                                    "dependencies": {"service_msgs_assembly": "0.0.0.0"},
                                },
                                "service_msgs_assembly/0.0.0.0": {"runtime": {}},
                            },
                        },
                        "libraries": {
                            "stereo_msgs_assembly/1.0.0": {"sha512": ""},
                            "service_msgs_assembly/0.0.0.0": {"sha512": ""},
                        },
                    }
                ),
                encoding="utf-8",
            )
            inventory = support / "r2fu-lyrical-win64-runtime-inventory.json"
            inventory.write_text(
                json.dumps(
                    {
                        "files": [
                            {
                                "path": "Ros2ForUnity/Plugins/stereo_msgs_assembly.deps.json",
                                "sha256": "",
                                "size": 0,
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )

            self.builder.patch_deps_json_sha512(package)

            patched = json.loads(deps.read_text(encoding="utf-8"))
            target = patched["targets"][".NETStandard,Version=v2.0/"]
            self.assertNotIn("service_msgs_assembly", target["stereo_msgs_assembly/1.0.0"]["dependencies"])
            self.assertNotIn("service_msgs_assembly/0.0.0.0", target)
            self.assertNotIn("service_msgs_assembly/0.0.0.0", patched["libraries"])
            patched_inventory = json.loads(inventory.read_text(encoding="utf-8"))
            self.assertEqual(self.builder.sha256_file(deps), patched_inventory["files"][0]["sha256"])

    def test_validate_ros2cs_metadata_descriptions_accepts_shared_ros2cs_release_label(self) -> None:
        """The ros2cs release label is provenance, not the selected ROS distro."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            metadata_files = (
                package / "Runtime" / "Ros2ForUnity" / "metadata_ros2cs.xml",
                package / "Runtime" / "Ros2ForUnity" / "Plugins" / "metadata_ros2cs.xml",
                package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64" / "metadata_ros2cs.xml",
            )
            for path in metadata_files:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(
                    "<ros2cs><ros2>lyrical</ros2><version><desc>v0.6.0-jazzy-preview</desc></version></ros2cs>",
                    encoding="utf-8",
                )

            self.builder.validate_ros2cs_metadata_descriptions(package)

    def test_validate_ros2cs_metadata_descriptions_rejects_cross_distro_field(self) -> None:
        """Generated Lyrical metadata must reject a genuinely cross-distro field."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            metadata_files = (
                package / "Runtime" / "Ros2ForUnity" / "metadata_ros2cs.xml",
                package / "Runtime" / "Ros2ForUnity" / "Plugins" / "metadata_ros2cs.xml",
                package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64" / "metadata_ros2cs.xml",
            )
            for path in metadata_files:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(
                    "<ros2cs><ros2>jazzy</ros2><version><desc>v0.6.0-jazzy-preview</desc></version></ros2cs>",
                    encoding="utf-8",
                )

            with self.assertRaises(ValueError):
                self.builder.validate_ros2cs_metadata_descriptions(package)


__all__ = ["LyricalDependencyDescriptionTests"]
