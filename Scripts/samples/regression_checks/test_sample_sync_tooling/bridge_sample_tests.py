from __future__ import annotations

from .foundation import *


class BridgeSampleSyncTests:
    """Exercise ROS2 bridge sample synchronization and import roots"""

    def test_ros2_bridge_sample_has_dedicated_sync_tool(self) -> None:
        """The Bridge sample must not rely on an untracked manual copy step."""
        script = ROOT / "Scripts/samples/sync_ros2_bridge_sample.py"

        self.assertTrue(script.is_file(), script)

    def test_maze_metadata_topic_matches_runtime_default(self) -> None:
        """Maze metadata, bootstrap, editor, and imported copy share the Draco topic."""
        manifest = json.loads(
            (ROOT / "Packages/dev.unity2foxglove.sdk/package.json").read_text(encoding="utf-8")
        )
        sample = next(item for item in manifest["samples"] if item["displayName"] == "Virtual LiDAR Maze Demo")
        metadata_topic = re.search(r"via (/[A-Za-z0-9_/-]+)", sample["description"])
        self.assertIsNotNone(metadata_topic)
        expected = metadata_topic.group(1)
        sources = [
            ROOT / "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs",
            ROOT / "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs",
            ROOT / "Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.6/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs",
            ROOT / "Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.6/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs",
        ]
        for source_path in sources:
            source = source_path.read_text(encoding="utf-8")
            self.assertIn(expected, source, source_path)

    def test_phase138c_helper_labels_diagnostic_mirror_not_product_gate(self) -> None:
        """The Phase138C helper must not present its duplicate publisher as product proof."""
        source = (ROOT / "Scripts/smoke/ros2/phase138c_pointcloud2_mirror_acceptance.py").read_text(
            encoding="utf-8"
        )
        self.assertIn("DIAGNOSTIC_MIRROR_GREEN", source)
        self.assertIn("PRODUCT_GATE_REQUIRED", source)
        self.assertNotIn("GREEN: /points external ROS2 acceptance checks completed", source)

    def test_ros2_bridge_sample_import_root_tracks_package_version(self) -> None:
        """The checked-in imported copy must follow the Bridge manifest version."""
        module = load_module(
            "sync_ros2_bridge_sample_version_under_test",
            "Scripts/samples/sync_ros2_bridge_sample.py",
        )
        self.assertTrue(hasattr(module, "default_imported_root"))

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            manifest = (
                root
                / "Packages"
                / "dev.unity2foxglove.ros2bridge"
                / "package.json"
            )
            manifest.parent.mkdir(parents=True)
            manifest.write_text(
                json.dumps({"version": "7.6.5-preview.4"}),
                encoding="utf-8",
            )

            imported = module.default_imported_root(root)

        self.assertTrue(
            imported.as_posix().endswith(
                "Unity2Foxglove ROS2 Bridge/7.6.5-preview.4/ROS2 Bridge Sample"
            )
        )

    def test_ros2_bridge_sample_sync_includes_meta_files(self) -> None:
        """New sample assets and their GUID-bearing meta files move together."""
        module = load_module(
            "sync_ros2_bridge_sample_meta_under_test",
            "Scripts/samples/sync_ros2_bridge_sample.py",
        )
        self.assertTrue(hasattr(module, "compare_roots"))
        self.assertTrue(hasattr(module, "apply_sync"))

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "package"
            imported = root / "imported"
            package.mkdir()
            imported.mkdir()
            (package / "Duplex.cs").write_text("current\n", encoding="utf-8")
            (package / "Duplex.cs.meta").write_text(
                "guid: 0123456789abcdef0123456789abcdef\n",
                encoding="utf-8",
            )

            drift = module.compare_roots(package, imported)
            module.apply_sync(package, imported, drift)
            self.assertEqual([], module.compare_roots(package, imported))

    def test_ros2_bridge_sample_capture_updates_only_generated_scene(self) -> None:
        """Unity owns scene generation while package source remains canonical."""
        module = load_module(
            "sync_ros2_bridge_sample_scene_under_test",
            "Scripts/samples/sync_ros2_bridge_sample.py",
        )
        self.assertTrue(hasattr(module, "capture_generated_scene"))

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "package"
            imported = root / "imported"
            for sample_root in (package, imported):
                (sample_root / "Scenes").mkdir(parents=True)
                (sample_root / "Scripts").mkdir(parents=True)
            (package / "Scenes/Ros2BridgeSample.unity").write_text(
                "old scene\n",
                encoding="utf-8",
            )
            (imported / "Scenes/Ros2BridgeSample.unity").write_text(
                "unity generated scene\n",
                encoding="utf-8",
            )
            (package / "Scripts/Duplex.cs").write_text(
                "package source\n",
                encoding="utf-8",
            )
            (imported / "Scripts/Duplex.cs").write_text(
                "local drift\n",
                encoding="utf-8",
            )

            module.capture_generated_scene(package, imported)

            self.assertEqual(
                "unity generated scene\n",
                (package / "Scenes/Ros2BridgeSample.unity").read_text(
                    encoding="utf-8"
                ),
            )
            self.assertEqual(
                "package source\n",
                (package / "Scripts/Duplex.cs").read_text(encoding="utf-8"),
            )

    def test_ros2_bridge_sample_rejects_missing_or_aliased_roots(self) -> None:
        """Bridge sample synchronization must fail before unsafe writes."""
        module = load_module(
            "sync_ros2_bridge_sample_roots_under_test",
            "Scripts/samples/sync_ros2_bridge_sample.py",
        )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            existing = root / "sample"
            existing.mkdir()
            with self.assertRaises(FileNotFoundError):
                module._validate_roots(root / "missing", existing)
            with self.assertRaises(ValueError):
                module._validate_roots(existing, existing)


__all__ = ["BridgeSampleSyncTests"]
