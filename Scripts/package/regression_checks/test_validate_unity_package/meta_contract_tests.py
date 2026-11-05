"""Regression checks for Unity package metadata and GUID contracts."""

from __future__ import annotations

from .foundation import *


class PackageMetaContractTests:
    """Exercise package .meta presence, importer, orphan, and GUID uniqueness contracts."""

    def test_bridge_asset_inventory_excludes_documentation(self) -> None:
        """Exclude documentation files from the Bridge Unity asset inventory."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2bridge"
            documentation = package / "Documentation~" / "Examples" / "Route.proto"
            runtime = package / "Runtime" / "Route.proto"
            documentation.parent.mkdir(parents=True)
            runtime.parent.mkdir(parents=True)
            documentation.write_text("syntax = \"proto3\";", encoding="utf-8")
            runtime.write_text("syntax = \"proto3\";", encoding="utf-8")

            assets = self.validator.iter_bridge_asset_files(package)

        self.assertNotIn(documentation, assets)
        self.assertIn(runtime, assets)

    def test_bridge_asset_inventory_excludes_ros2_package_outputs(self) -> None:
        """Exclude generated Ros2Package files and folders from the Bridge asset inventory."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2bridge"
            generated = package / "Ros2Package~" / "Generated" / "Route.proto"
            runtime = package / "Runtime" / "Route.proto"
            generated.parent.mkdir(parents=True)
            runtime.parent.mkdir(parents=True)
            generated.write_text("syntax = \"proto3\";", encoding="utf-8")
            runtime.write_text("syntax = \"proto3\";", encoding="utf-8")

            assets = self.validator.iter_bridge_asset_files(package)
            folders = self.validator.iter_bridge_asset_directories(package)

        self.assertNotIn(generated, assets)
        self.assertIn(runtime, assets)
        self.assertNotIn(generated.parents[1], folders)
        self.assertNotIn(generated.parents[2], folders)

    def test_bridge_guid_scan_rejects_orphan_sidecars(self) -> None:
        """Reject orphan Unity metadata sidecars during the Bridge GUID scan."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2bridge"
            orphan = package / "Runtime" / "Missing.cs.meta"
            orphan.parent.mkdir(parents=True)
            orphan.write_text(
                "fileFormatVersion: 2\n"
                "guid: 66666666666666666666666666666666\n",
                encoding="utf-8",
            )
            results = []
            self.validator.check_package_meta_guid_uniqueness(
                results,
                package,
                "Bridge GUIDs",
                "Bridge",
            )

        self.assertFalse(results[-1].ok)
        self.assertIn("Missing.cs.meta", results[-1].detail)

    def test_bridge_meta_requires_asset_and_folder_sidecars(self) -> None:
        """Bridge Unity assets and tracked folders need metadata sidecars."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2bridge"
            runtime = package / "Runtime"
            source = runtime / "Example.cs"
            protocol = runtime / "Example.proto"
            runtime.mkdir(parents=True)
            source.write_text("class Example {}", encoding="utf-8")
            protocol.write_text("syntax = \"proto3\";", encoding="utf-8")

            with mock.patch.object(self.validator, "ROS2_BRIDGE_PACKAGE", package):
                results = []
                self.validator.check_ros2_bridge_meta(results)
            files = next(
                item for item in results
                if item.name == "ROS2 Bridge Unity asset and folder .meta files"
            )
            self.assertFalse(files.ok)
            self.assertIn("Example.proto.meta", files.detail)

            (runtime.with_suffix(".meta")).write_text(
                "fileFormatVersion: 2\n"
                "guid: 11111111111111111111111111111111\n"
                "folderAsset: yes\n",
                encoding="utf-8",
            )
            (Path(str(source) + ".meta")).write_text(
                "fileFormatVersion: 2\n"
                "guid: 22222222222222222222222222222222\n",
                encoding="utf-8",
            )
            (Path(str(protocol) + ".meta")).write_text(
                "fileFormatVersion: 2\n"
                "guid: 33333333333333333333333333333333\n",
                encoding="utf-8",
            )
            with mock.patch.object(self.validator, "ROS2_BRIDGE_PACKAGE", package):
                results = []
                self.validator.check_ros2_bridge_meta(results)
            self.assertTrue(
                next(
                    item for item in results
                    if item.name == "ROS2 Bridge Unity asset and folder .meta files"
                ).ok
            )
            self.assertTrue(
                next(
                    item for item in results
                    if item.name == "ROS2 Bridge Unity asset and folder .meta GUIDs"
                ).ok
            )

    def test_bridge_meta_guid_uniqueness_rejects_duplicate_sidecars(self) -> None:
        """Bridge assets and folders must not share Unity GUIDs."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2bridge"
            runtime = package / "Runtime"
            first = runtime / "First.cs"
            second = runtime / "Second.cs"
            runtime.mkdir(parents=True)
            first.write_text("class First {}", encoding="utf-8")
            second.write_text("class Second {}", encoding="utf-8")
            (runtime.with_suffix(".meta")).write_text(
                "fileFormatVersion: 2\n"
                "guid: 44444444444444444444444444444444\n"
                "folderAsset: yes\n",
                encoding="utf-8",
            )
            for source in (first, second):
                (Path(str(source) + ".meta")).write_text(
                    "fileFormatVersion: 2\n"
                    "guid: 55555555555555555555555555555555\n",
                    encoding="utf-8",
                )

            with mock.patch.object(self.validator, "ROS2_BRIDGE_PACKAGE", package):
                results = []
                self.validator.check_ros2_bridge_meta(results)

        guids = next(
            item for item in results
            if item.name == "ROS2 Bridge Unity asset and folder .meta GUIDs"
        )
        self.assertFalse(guids.ok)
        self.assertIn("duplicates", guids.detail)

    def test_bridge_meta_rejects_non_mono_csharp_importers(self) -> None:
        """Bridge C# metadata must not declare a non-Mono importer."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2bridge"
            source = package / "Runtime" / "Example.cs"
            source.parent.mkdir(parents=True)
            source.write_text("class Example {}", encoding="utf-8")
            (Path(str(source) + ".meta")).write_text(
                "fileFormatVersion: 2\n"
                "guid: 12121212121212121212121212121212\n"
                "TextureImporter:\n",
                encoding="utf-8",
            )

            with mock.patch.object(self.validator, "ROS2_BRIDGE_PACKAGE", package):
                results = []
                self.validator.check_ros2_bridge_meta(results)

        files = next(
            item for item in results
            if item.name == "ROS2 Bridge Unity asset and folder .meta files"
        )
        self.assertFalse(files.ok)
        self.assertIn("Example.cs.meta", files.detail)

    def test_bridge_meta_rejects_inline_importer_values(self) -> None:
        """Bridge C# metadata must reject importer keys with inline values."""
        for importer in ("DefaultImporter: {}", "DefaultImporter: # comment"):
            with tempfile.TemporaryDirectory() as temp:
                package = Path(temp) / "dev.unity2foxglove.ros2bridge"
                source = package / "Runtime" / "Example.cs"
                source.parent.mkdir(parents=True)
                source.write_text("class Example {}", encoding="utf-8")
                (Path(str(source) + ".meta")).write_text(
                    "fileFormatVersion: 2\n"
                    "guid: 12121212121212121212121212121212\n"
                    f"{importer}\n",
                    encoding="utf-8",
                )
                with mock.patch.object(self.validator, "ROS2_BRIDGE_PACKAGE", package):
                    results = []
                    self.validator.check_ros2_bridge_meta(results)
            files = next(
                item for item in results
                if item.name == "ROS2 Bridge Unity asset and folder .meta files"
            )
            self.assertFalse(files.ok)
            self.assertIn("Example.cs.meta", files.detail)

    def test_sdk_meta_checks_csharp_importer_type(self) -> None:
        """SDK C# source metas must use the MonoImporter type."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            sdk = root / "Packages" / "dev.unity2foxglove.sdk"
            source = sdk / "Runtime" / "Example.cs"
            source.parent.mkdir(parents=True)
            source.write_text("class Example {}", encoding="utf-8")
            (Path(str(source) + ".meta")).write_text(
                "fileFormatVersion: 2\n"
                "guid: 0123456789abcdef0123456789abcdef\n"
                "DefaultImporter:\n",
                encoding="utf-8",
            )

            results = []
            self.validator.check_sdk_script_meta(results, [source])

        self.assertFalse(results[-1].ok)
        self.assertIn("Example.cs.meta", results[-1].detail)

    def test_sdk_decomposed_folder_meta_checks_folder_sidecars(self) -> None:
        """Decomposed SDK test folders need valid, unique folder .meta files."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "Ros2ForUnity"
            child = root / "ExampleTests"
            child.mkdir(parents=True)

            results = []
            self.validator.check_sdk_decomposed_folder_meta(results, root)
            self.assertFalse(results[-1].ok)
            self.assertIn("Ros2ForUnity.meta", results[-1].detail)

            (root.with_suffix(".meta")).write_text(
                "fileFormatVersion: 2\nguid: 11111111111111111111111111111111\nfolderAsset: yes\n",
                encoding="utf-8",
            )
            (child.with_suffix(".meta")).write_text(
                "fileFormatVersion: 2\nguid: 22222222222222222222222222222222\nfolderAsset: yes\n",
                encoding="utf-8",
            )
            results = []
            self.validator.check_sdk_decomposed_folder_meta(results, root)
            self.assertTrue(results[-1].ok)

            (child.with_suffix(".meta")).write_text(
                "fileFormatVersion: 2\nguid: 11111111111111111111111111111111\nfolderAsset: yes\n",
                encoding="utf-8",
            )
            results = []
            self.validator.check_sdk_decomposed_folder_meta(results, root)
            self.assertFalse(results[-1].ok)
            self.assertIn("duplicates", results[-1].detail)

    def test_sdk_decomposed_folder_meta_checks_nested_unit_roots_by_default(self) -> None:
        """The default check must include every nested Tests/Unit Decomposed root."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "Packages" / "dev.unity2foxglove.sdk"
            unit = package / "Tests" / "Unit"
            required_root = unit / "Decomposed"
            nested_root = unit / "Mcap" / "Decomposed"
            required_root.mkdir(parents=True)
            nested_root.mkdir(parents=True)
            (required_root.with_suffix(".meta")).write_text(
                "fileFormatVersion: 2\n"
                "guid: 11111111111111111111111111111111\n"
                "folderAsset: yes\n",
                encoding="utf-8",
            )

            with mock.patch.object(self.validator, "PACKAGE", package), mock.patch.object(
                self.validator, "SDK_DECOMPOSED_TESTS", required_root
            ):
                results = []
                self.validator.check_sdk_decomposed_folder_meta(results)
                self.assertFalse(results[-1].ok)
                self.assertIn("Mcap/Decomposed.meta", results[-1].detail)

                (nested_root.with_suffix(".meta")).write_text(
                    "fileFormatVersion: 2\n"
                    "guid: 22222222222222222222222222222222\n"
                    "folderAsset: yes\n",
                    encoding="utf-8",
                )
                results = []
                self.validator.check_sdk_decomposed_folder_meta(results)

            self.assertTrue(results[-1].ok)

    def test_sdk_decomposed_folder_meta_checks_runtime_and_performance_roots(self) -> None:
        """SDK runtime and performance decomposition folders need valid sidecars too."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "Packages" / "dev.unity2foxglove.sdk"
            required_root = package / "Tests" / "Unit" / "Decomposed"
            runtime_root = package / "Runtime" / "Schemas" / "Proto" / "Video" / "Decomposed"
            performance_root = package / "Tests" / "Performance" / "Decomposed"
            for root in (required_root, runtime_root, performance_root):
                root.mkdir(parents=True)
            for index, root in enumerate((required_root, runtime_root, performance_root), 1):
                (root.with_suffix(".meta")).write_text(
                    f"fileFormatVersion: 2\nguid: {index:032x}\nfolderAsset: yes\n",
                    encoding="utf-8",
                )
            with mock.patch.object(self.validator, "PACKAGE", package), mock.patch.object(
                self.validator, "SDK_DECOMPOSED_TESTS", required_root
            ):
                results = []
                self.validator.check_sdk_decomposed_folder_meta(results)
                self.assertTrue(results[-1].ok)
                (runtime_root.with_suffix(".meta")).write_text(
                    "fileFormatVersion: 2\nguid: 22222222222222222222222222222222\n",
                    encoding="utf-8",
                )
                results = []
                self.validator.check_sdk_decomposed_folder_meta(results)
                self.assertFalse(results[-1].ok)
                self.assertIn("Runtime/Schemas/Proto/Video/Decomposed.meta", results[-1].detail)

    def test_sdk_decomposed_folder_meta_requires_root_directory(self) -> None:
        """The required decomposed test root must fail closed when absent."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "MissingDecomposed"
            results = []
            self.validator.check_sdk_decomposed_folder_meta(results, root)

        self.assertFalse(results[-1].ok)
        self.assertIn("Required directory missing", results[-1].detail)

    def test_sdk_decomposed_folder_meta_rejects_csharp_guid_collision(self) -> None:
        """Folder GUIDs must not collide with SDK C# source GUIDs."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "Decomposed"
            root.mkdir(parents=True)
            source = Path(temp) / "Example.cs"
            source.write_text("class Example {}", encoding="utf-8")
            guid = "33333333333333333333333333333333"
            (Path(str(source) + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\nMonoImporter:\n",
                encoding="utf-8",
            )
            (root.with_suffix(".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\n",
                encoding="utf-8",
            )

            known_guids = {}
            results = []
            self.validator.check_sdk_script_meta(results, [source], known_guids)
            self.assertTrue(results[-1].ok)
            self.validator.check_sdk_decomposed_folder_meta(results, root, known_guids)

        self.assertFalse(results[-1].ok)
        self.assertIn("duplicates", results[-1].detail)

    def test_sdk_non_csharp_asset_meta_requires_proto_sidecars(self) -> None:
        """Non-C# SDK Unity assets must have metadata sidecars."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "Packages" / "dev.unity2foxglove.sdk"
            asset = package / "Runtime" / "Route.proto"
            asset.parent.mkdir(parents=True)
            asset.write_text("syntax = \"proto3\";", encoding="utf-8")

            with mock.patch.object(self.validator, "PACKAGE", package):
                results = []
                self.validator.check_sdk_non_csharp_asset_meta(results)
            self.assertFalse(results[-1].ok)
            self.assertIn("Route.proto.meta", results[-1].detail)

            Path(str(asset) + ".meta").write_text(
                "fileFormatVersion: 2\n"
                "guid: malformed\n",
                encoding="utf-8",
            )
            with mock.patch.object(self.validator, "PACKAGE", package):
                results = []
                self.validator.check_sdk_non_csharp_asset_meta(results)
            self.assertFalse(results[-1].ok)
            self.assertIn("Route.proto.meta", results[-1].detail)

            Path(str(asset) + ".meta").write_text(
                "fileFormatVersion: 2\n"
                "guid: 78787878787878787878787878787878\n",
                encoding="utf-8",
            )
            with mock.patch.object(self.validator, "PACKAGE", package):
                results = []
                self.validator.check_sdk_non_csharp_asset_meta(results)

        self.assertTrue(results[-1].ok)

    def test_sdk_decomposed_folder_meta_rejects_malformed_metadata(self) -> None:
        """Folder metadata must contain a valid GUID and folderAsset marker."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "Decomposed"
            root.mkdir(parents=True)
            meta = root.with_suffix(".meta")
            meta.write_text(
                "fileFormatVersion: 2\nguid: malformed\nfolderAsset: yes\n",
                encoding="utf-8",
            )
            results = []
            self.validator.check_sdk_decomposed_folder_meta(results, root)
            self.assertFalse(results[-1].ok)

            meta.write_text(
                "fileFormatVersion: 2\nguid: 44444444444444444444444444444444\n",
                encoding="utf-8",
            )
            results = []
            self.validator.check_sdk_decomposed_folder_meta(results, root)

        self.assertFalse(results[-1].ok)

    def test_sdk_meta_guid_uniqueness_rejects_asmdef_folder_collision(self) -> None:
        """SDK Unity assets and folders must not share a GUID."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.sdk"
            folder = package / "Tests" / "Unit" / "Decomposed" / "Example"
            asmdef = package / "Runtime" / "Example.asmdef"
            folder.mkdir(parents=True)
            asmdef.parent.mkdir(parents=True)
            asmdef.write_text("{}", encoding="utf-8")
            guid = "55555555555555555555555555555555"
            (Path(str(folder) + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\n",
                encoding="utf-8",
            )
            (Path(str(asmdef) + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\n",
                encoding="utf-8",
            )

            results = []
            self.validator.check_sdk_meta_guid_uniqueness(results, package)

        self.assertFalse(results[-1].ok)
        self.assertIn("duplicates", results[-1].detail)

    def test_sdk_meta_guid_uniqueness_rejects_orphan_folder_sidecars(self) -> None:
        """Unknown folder metadata without a matching directory must fail."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.sdk"
            orphan = package / "Runtime" / "MissingFolder.meta"
            orphan.parent.mkdir(parents=True)
            orphan.write_text(
                "fileFormatVersion: 2\n"
                "guid: 88888888888888888888888888888888\n",
                encoding="utf-8",
            )

            results = []
            self.validator.check_sdk_meta_guid_uniqueness(results, package)

        self.assertFalse(results[-1].ok)
        self.assertIn("MissingFolder.meta", results[-1].detail)

    def test_sdk_meta_guid_uniqueness_allows_known_optional_folder_sidecars(self) -> None:
        """Known empty Unity folder sidecars remain valid package metadata."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.sdk"
            optional = package / "Editor" / "SourceGenerators" / "build.meta"
            optional.parent.mkdir(parents=True)
            optional.write_text(
                "fileFormatVersion: 2\n"
                "guid: 99999999999999999999999999999999\n"
                "folderAsset: yes\n",
                encoding="utf-8",
            )

            results = []
            self.validator.check_sdk_meta_guid_uniqueness(results, package)

        self.assertTrue(results[-1].ok)

    def test_sdk_meta_guid_uniqueness_checks_optional_folder_guid_collisions(self) -> None:
        """Allowed missing folder sidecars must still participate in GUID checks."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.sdk"
            optional = package / "Editor" / "SourceGenerators" / "build.meta"
            source = package / "Runtime" / "Example.cs"
            optional.parent.mkdir(parents=True)
            source.parent.mkdir(parents=True)
            source.write_text("class Example {}", encoding="utf-8")
            guid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            optional.write_text(
                f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\n",
                encoding="utf-8",
            )
            (Path(str(source) + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\n",
                encoding="utf-8",
            )

            results = []
            self.validator.check_sdk_meta_guid_uniqueness(results, package)

        self.assertFalse(results[-1].ok)
        self.assertIn("duplicates", results[-1].detail)

    def test_sdk_meta_guid_uniqueness_rejects_malformed_optional_folder_sidecars(self) -> None:
        """Allowed missing folder sidecars must retain valid folder metadata."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.sdk"
            optional = package / "Editor" / "SourceGenerators" / "build.meta"
            optional.parent.mkdir(parents=True)
            optional.write_text(
                "fileFormatVersion: 2\n"
                "guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n",
                encoding="utf-8",
            )

            results = []
            self.validator.check_sdk_meta_guid_uniqueness(results, package)

        self.assertFalse(results[-1].ok)
        self.assertIn("build.meta", results[-1].detail)

    def test_sdk_meta_guid_uniqueness_ignores_non_unity_sidecars(self) -> None:
        """Build-tool sidecars are outside the Unity asset GUID scope."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.sdk"
            props = package / "Tests" / "Directory.Build.props"
            json_asset = package / "Samples~" / "Layout.json"
            props.parent.mkdir(parents=True)
            json_asset.parent.mkdir(parents=True)
            guid = "66666666666666666666666666666666"
            props.write_text("<Project />", encoding="utf-8")
            json_asset.write_text("{}", encoding="utf-8")
            (Path(str(props) + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\n",
                encoding="utf-8",
            )
            (Path(str(json_asset) + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\n",
                encoding="utf-8",
            )

            results = []
            self.validator.check_sdk_meta_guid_uniqueness(results, package)

        self.assertTrue(results[-1].ok)

    def test_sdk_meta_guid_uniqueness_rejects_proto_asmdef_collision(self) -> None:
        """Protocol assets and assembly definitions must not share a GUID."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.sdk"
            proto = package / "Runtime" / "Schemas" / "Example.proto"
            asmdef = package / "Runtime" / "Example.asmdef"
            proto.parent.mkdir(parents=True)
            proto.write_text("syntax = \"proto3\";", encoding="utf-8")
            asmdef.write_text("{}", encoding="utf-8")
            guid = "77777777777777777777777777777777"
            (Path(str(proto) + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\n",
                encoding="utf-8",
            )
            (Path(str(asmdef) + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\n",
                encoding="utf-8",
            )

            results = []
            self.validator.check_sdk_meta_guid_uniqueness(results, package)

        self.assertFalse(results[-1].ok)
        self.assertIn("duplicates", results[-1].detail)

    def test_sdk_meta_guid_uniqueness_rejects_pb_cs_collision(self) -> None:
        """Generated protocol assets and C# sources must not share a GUID."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.sdk"
            descriptor = package / "Runtime" / "Schemas" / "Example.pb"
            source = package / "Runtime" / "Example.cs"
            descriptor.parent.mkdir(parents=True)
            descriptor.write_bytes(b"descriptor")
            source.write_text("class Example {}", encoding="utf-8")
            guid = "88888888888888888888888888888888"
            (Path(str(descriptor) + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\n",
                encoding="utf-8",
            )
            (Path(str(source) + ".meta")).write_text(
                f"fileFormatVersion: 2\nguid: {guid}\n",
                encoding="utf-8",
            )

            results = []
            self.validator.check_sdk_meta_guid_uniqueness(results, package)

        self.assertFalse(results[-1].ok)
        self.assertIn("duplicates", results[-1].detail)

    def test_all_package_meta_requires_csharp_and_folder_sidecars(self) -> None:
        """Every first-party package asset and folder must retain Unity metadata."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2forunity"
            source = package / "Runtime" / "Example.cs"
            folder = package / "Runtime" / "Nested"
            folder.mkdir(parents=True)
            source.write_text("class Example {}", encoding="utf-8")
            results = []
            self.validator.check_package_asset_meta(results, package, "R2FU")
            self.assertFalse(results[-1].ok)
            self.assertIn("Example.cs.meta", results[-1].detail)
            self.assertIn("Nested.meta", results[-1].detail)

    def test_all_package_meta_rejects_default_importer_for_csharp(self) -> None:
        """C# sidecars in every package must use MonoImporter metadata."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2forunity"
            source = package / "Samples~" / "Example.cs"
            source.parent.mkdir(parents=True)
            source.write_text("class Example {}", encoding="utf-8")
            (Path(str(source) + ".meta")).write_text(
                "fileFormatVersion: 2\n"
                "guid: 12121212121212121212121212121212\n"
                "DefaultImporter:\n",
                encoding="utf-8",
            )
            results = []
            self.validator.check_package_asset_meta(results, package, "R2FU")
        self.assertFalse(results[-1].ok)
        self.assertIn("Example.cs.meta", results[-1].detail)

    def test_all_package_guid_scan_rejects_cross_package_duplicate(self) -> None:
        """A GUID reused by two first-party packages must fail closed."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "Packages"
            first = root / "dev.unity2foxglove.sdk" / "Runtime" / "One.cs"
            second = root / "dev.unity2foxglove.ros2bridge" / "Runtime" / "Two.cs"
            first.parent.mkdir(parents=True)
            second.parent.mkdir(parents=True)
            first.write_text("class One {}", encoding="utf-8")
            second.write_text("class Two {}", encoding="utf-8")
            for source in (first, second):
                Path(str(source) + ".meta").write_text(
                    "fileFormatVersion: 2\n"
                    "guid: 12121212121212121212121212121212\n"
                    "MonoImporter:\n",
                    encoding="utf-8",
                )
            results = []
            self.validator.check_packages_meta_guid_uniqueness(results, root)
        self.assertFalse(results[-1].ok)
        self.assertIn("duplicates", results[-1].detail)

    def test_all_package_guid_scan_rejects_non_unity_extension_meta_collision(self) -> None:
        """Metadata beside non-Unity extensions must participate in GUID checks."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "Packages"
            first = root / "dev.unity2foxglove.sdk" / "README.md"
            second = root / "dev.unity2foxglove.ros2bridge" / "LICENSE"
            first.parent.mkdir(parents=True)
            second.parent.mkdir(parents=True)
            first.write_text("# SDK", encoding="utf-8")
            second.write_text("license", encoding="utf-8")
            for asset in (first, second):
                Path(str(asset) + ".meta").write_text(
                    "fileFormatVersion: 2\n"
                    "guid: 34343434343434343434343434343434\n",
                    encoding="utf-8",
                )
            results = []
            self.validator.check_packages_meta_guid_uniqueness(results, root)

        self.assertFalse(results[-1].ok)
        self.assertIn("duplicates", results[-1].detail)

    def test_all_package_guid_scan_converts_metadata_read_errors_to_failures(self) -> None:
        """Package GUID metadata read errors must become structured failures."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "Packages"
            meta = root / "dev.unity2foxglove.sdk" / "Runtime" / "Example.cs.meta"
            meta.parent.mkdir(parents=True)
            meta.with_suffix("").write_text("class Example {}", encoding="utf-8")
            meta.write_text("fileFormatVersion: 2\nguid: 78787878787878787878787878787878\n", encoding="utf-8")

            original_read_text = Path.read_text

            def fail_target(path: Path, *args, **kwargs) -> str:
                """Raise for the target metadata while delegating other reads."""
                if path == meta:
                    raise OSError("denied")
                return original_read_text(path, *args, **kwargs)

            with mock.patch.object(Path, "read_text", autospec=True, side_effect=fail_target):
                results = []
                self.validator.check_packages_meta_guid_uniqueness(results, root)

        self.assertFalse(results[-1].ok)
        self.assertIn("could not be read", results[-1].detail)

    def test_package_asset_meta_rejects_orphan_sidecar(self) -> None:
        """R2FU and Remote Gateway package scans must reject orphan metadata."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2forunity"
            orphan = package / "Runtime" / "Deleted.cs.meta"
            orphan.parent.mkdir(parents=True)
            orphan.write_text(
                "fileFormatVersion: 2\n"
                "guid: 56565656565656565656565656565656\n",
                encoding="utf-8",
            )
            results = []
            self.validator.check_package_asset_meta(results, package, "R2FU")

        self.assertFalse(results[-1].ok)
        self.assertIn("Deleted.cs.meta", results[-1].detail)

    def test_package_asset_meta_converts_metadata_read_errors_to_failures(self) -> None:
        """Metadata read errors must become structured validation failures."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2forunity"
            asset = package / "Runtime" / "Example.asset"
            asset.parent.mkdir(parents=True)
            asset.write_text("asset", encoding="utf-8")
            Path(str(asset) + ".meta").write_text(
                "fileFormatVersion: 2\n"
                "guid: 56565656565656565656565656565656\n",
                encoding="utf-8",
            )

            with mock.patch.object(Path, "read_text", side_effect=OSError("denied")):
                results = []
                self.validator.check_package_asset_meta(results, package, "R2FU")

        self.assertFalse(results[-1].ok)
        self.assertIn("Example.asset.meta", results[-1].detail)

    def test_package_asset_meta_converts_folder_metadata_read_errors_to_failures(self) -> None:
        """Folder metadata read errors must become structured validation failures."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2forunity"
            folder = package / "Runtime" / "Nested"
            folder.mkdir(parents=True)
            for directory, guid in (
                (folder.parent, "56565656565656565656565656565656"),
                (folder, "67676767676767676767676767676767"),
            ):
                Path(str(directory) + ".meta").write_text(
                    "fileFormatVersion: 2\n"
                    f"guid: {guid}\n"
                    "folderAsset: yes\n",
                    encoding="utf-8",
                )

            original_read_text = Path.read_text

            def fail_nested_folder_meta(path: Path, *args, **kwargs) -> str:
                """Raise for the nested folder metadata while delegating other reads."""
                if path == Path(str(folder) + ".meta"):
                    raise OSError("denied")
                return original_read_text(path, *args, **kwargs)

            with mock.patch.object(
                Path, "read_text", autospec=True, side_effect=fail_nested_folder_meta
            ):
                results = []
                self.validator.check_package_asset_meta(results, package, "R2FU")

        self.assertFalse(results[-1].ok)
        self.assertIn("Nested.meta", results[-1].detail)

    def test_decomposed_folder_meta_converts_read_errors_to_failures(self) -> None:
        """Decomposed folder metadata read errors must become structured failures."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "Decomposed"
            root.mkdir(parents=True)
            meta = Path(str(root) + ".meta")
            meta.write_text(
                "fileFormatVersion: 2\n"
                "guid: 89898989898989898989898989898989\n"
                "folderAsset: yes\n",
                encoding="utf-8",
            )
            with mock.patch.object(Path, "read_text", side_effect=OSError("denied")):
                results = []
                self.validator.check_sdk_decomposed_folder_meta(results, root)

        self.assertFalse(results[-1].ok)
        self.assertIn("Decomposed.meta", results[-1].detail)

    def test_csharp_meta_without_importer_is_valid(self) -> None:
        """Minimal C# metadata remains valid when Unity omits an importer block."""
        text = (
            "fileFormatVersion: 2\n"
            "guid: 78787878787878787878787878787878\n"
        )
        self.assertTrue(self.validator._csharp_meta_importer_is_valid(text))


__all__ = ["PackageMetaContractTests"]
