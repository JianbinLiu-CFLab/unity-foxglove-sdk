from __future__ import annotations

from .foundation import *


class PackageStructureTests:
    """Exercise package structure, metadata, version, and matrix contracts."""

    def test_linker_processor_owns_effective_preservation_input(self) -> None:
        """The SDK must register an explicit UnityLinker processor for reflection roots."""
        source = (
            ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "FoxRun"
            / "FoxrunBuildPreprocess.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("IUnityLinkerProcessor", source)
        self.assertIn("GenerateAdditionalLinkXmlFile", source)
        self.assertIn("UnityLinkerBuildPipelineData", source)

    def test_build_artifact_guard_rejects_case_variant_directory(self) -> None:
        """Build/cache directory names are matched case-insensitively."""
        with tempfile.TemporaryDirectory() as temp:
            offender = Path(temp) / "Bin"
            offender.mkdir()
            results = []
            self.validator.check_package_build_artifacts(results, [offender])
        self.assertFalse(results[0].ok)

    def test_bridge_asset_inventory_excludes_build_artifacts(self) -> None:
        """Bridge package build outputs must not enter the Unity meta gate."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2bridge"
            generated = package / "Tests" / "Unit" / "obj" / "project.assets.json"
            generated.parent.mkdir(parents=True)
            generated.write_text("{}", encoding="utf-8")
            ordinary = package / "Samples~" / "Demo" / "Route.json"
            ordinary.parent.mkdir(parents=True)
            ordinary.write_text("{}", encoding="utf-8")

            assets = self.validator.iter_bridge_asset_files(package)

        self.assertNotIn(generated, assets)
        self.assertIn(ordinary, assets)

    def test_sample_meta_checks_asmdef_files(self) -> None:
        """Sample asmdef files need stable Unity .meta sidecars."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            sample = root / "Samples~" / "Demo"
            sample.mkdir(parents=True)
            (sample / "Demo.asmdef").write_text("{}", encoding="utf-8")

            self.validator.SAMPLES = root / "Samples~"
            results = []
            self.validator.check_sample_meta(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("Demo.asmdef", results[-1].detail)

    def test_sdk_meta_checks_csharp_files(self) -> None:
        """SDK C# sources need stable Unity .meta sidecars."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            sdk = root / "Packages" / "dev.unity2foxglove.sdk"
            sdk.mkdir(parents=True)
            source = sdk / "Runtime" / "Example.cs"
            source.parent.mkdir(parents=True)
            source.write_text("class Example {}", encoding="utf-8")

            self.validator.PACKAGE = sdk
            results = []
            self.validator.check_sdk_script_meta(results)
            self.assertFalse(results[-1].ok)
            self.assertIn("Example.cs", results[-1].detail)

            (Path(str(source) + ".meta")).write_text(
                "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n",
                encoding="utf-8",
            )
            results = []
            self.validator.check_sdk_script_meta(results)
            self.assertTrue(results[-1].ok)

    def test_sample_meta_checks_prefab_files(self) -> None:
        """Common Unity assets such as prefabs need stable .meta sidecars."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            sample = root / "Samples~" / "Demo"
            sample.mkdir(parents=True)
            (sample / "Robot.prefab").write_text("%YAML 1.1", encoding="utf-8")

            self.validator.SAMPLES = root / "Samples~"
            results = []
            self.validator.check_sample_meta(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("Robot.prefab", results[-1].detail)

    def test_package_version_must_be_semver(self) -> None:
        """Unity package versions should be MAJOR.MINOR.PATCH."""
        results = []
        self.validator.check_package_identity(
            results,
            {
                "name": "dev.unity2foxglove.sdk",
                "displayName": "Unity2Foxglove SDK",
                "license": "Apache-2.0",
                "version": "1.0",
                "samples": [],
            },
        )

        version_result = next(item for item in results if item.name == "package version")
        self.assertFalse(version_result.ok)

    def test_dependent_package_version_pin_must_match_sdk(self) -> None:
        """Optional packages in the repo should depend on the current SDK version."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            gateway = root / "Packages" / "dev.unity2foxglove.remotegateway.win64"
            gateway.mkdir(parents=True)
            (gateway / "package.json").write_text(
                '{"dependencies":{"dev.unity2foxglove.sdk":"1.9.5"}}',
                encoding="utf-8",
            )

            self.validator.REMOTE_GATEWAY_PACKAGE = gateway
            results = []
            self.validator.check_dependent_package_versions(results, {"version": "1.9.6"})

        self.assertFalse(results[-1].ok)
        self.assertIn("1.9.5", results[-1].detail)
        self.assertIn("1.9.6", results[-1].detail)

    def test_dependent_package_version_pin_accepts_current_sdk(self) -> None:
        """The remote gateway package can be validated independently of install order."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            gateway = root / "Packages" / "dev.unity2foxglove.remotegateway.win64"
            gateway.mkdir(parents=True)
            (gateway / "package.json").write_text(
                '{"dependencies":{"dev.unity2foxglove.sdk":"1.9.6"}}',
                encoding="utf-8",
            )

            self.validator.REMOTE_GATEWAY_PACKAGE = gateway
            results = []
            self.validator.check_dependent_package_versions(results, {"version": "1.9.6"})

        self.assertTrue(results[-1].ok)

    def test_package_matrix_gate_authenticates_all_four_package_manifests(self) -> None:
        """The standalone validator must reject drift outside the SDK manifest."""
        manifests = {
            "sdk": (
                "dev.unity2foxglove.sdk",
                "1.9.6",
                {
                    "com.unity.nuget.newtonsoft-json": "3.2.1",
                    "com.unity.burst": "1.8.18",
                    "com.unity.collections": "2.5.5",
                    "com.unity.mathematics": "1.3.2",
                },
            ),
            "r2fu": (
                "dev.unity2foxglove.ros2forunity",
                "0.1.0-preview.1",
                {"dev.unity2foxglove.sdk": "1.9.6"},
            ),
            "bridge": (
                "dev.unity2foxglove.ros2bridge",
                "0.1.0-preview.1",
                {"dev.unity2foxglove.sdk": "1.9.6"},
            ),
            "remote_gateway": (
                "dev.unity2foxglove.remotegateway.win64",
                "0.1.0-preview.1",
                {"dev.unity2foxglove.sdk": "1.9.6"},
            ),
        }
        relatives = {
            "sdk": "dev.unity2foxglove.sdk",
            "r2fu": "dev.unity2foxglove.ros2forunity",
            "bridge": "dev.unity2foxglove.ros2bridge",
            "remote_gateway": "dev.unity2foxglove.remotegateway.win64",
        }

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            paths = {key: root / relative for key, relative in relatives.items()}
            for key, package in paths.items():
                package.mkdir(parents=True)
                name, version, dependencies = manifests[key]
                (package / "package.json").write_text(
                    json.dumps(
                        {
                            "name": name,
                            "version": version,
                            "dependencies": dependencies,
                        }
                    ),
                    encoding="utf-8",
                )

            gateway_manifest = paths["remote_gateway"] / "package.json"
            gateway_data = json.loads(gateway_manifest.read_text(encoding="utf-8"))
            gateway_data["name"] = "dev.unity2foxglove.remotegateway.win64.decoy"
            gateway_manifest.write_text(json.dumps(gateway_data), encoding="utf-8")

            with mock.patch.object(self.validator, "PACKAGE", paths["sdk"]), \
                mock.patch.object(self.validator, "ROS2_FOR_UNITY_PACKAGE", paths["r2fu"], create=True), \
                mock.patch.object(self.validator, "ROS2_BRIDGE_PACKAGE", paths["bridge"]), \
                mock.patch.object(self.validator, "REMOTE_GATEWAY_PACKAGE", paths["remote_gateway"]):
                results = []
                checker = getattr(self.validator, "check_package_matrix", None)
                self.assertIsNotNone(checker, "standalone package matrix gate is missing")
                if checker is not None:
                    checker(results)

        matrix_result = next(
            item for item in results if item.name == "package matrix remote_gateway name"
        )
        self.assertFalse(matrix_result.ok)
        self.assertIn("remotegateway.win64.decoy", matrix_result.detail)

    def test_main_invokes_the_package_matrix_gate(self) -> None:
        """The release entry point must execute the independent matrix gate."""
        with mock.patch.object(self.validator, "check_package_matrix") as gate:
            with mock.patch.object(self.validator, "load_package_json", return_value={}):
                self.assertEqual(0, self.validator.main())
        gate.assert_called_once_with(mock.ANY)

    def test_ros2_bridge_package_requires_the_duplex_sample_surface(self) -> None:
        """A distributable Bridge sample includes its behavior, builder, and guide."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2bridge"
            package.mkdir()
            (package / "package.json").write_text(
                '{"name":"dev.unity2foxglove.ros2bridge",'
                '"version":"0.1.0-preview.1",'
                '"dependencies":{"dev.unity2foxglove.sdk":"1.9.6"},'
                '"samples":[{"displayName":"ROS2 Bridge Sample",'
                '"path":"Samples~/Ros2BridgeSample"}]}',
                encoding="utf-8",
            )
            old_required = (
                "Runtime/Unity2Foxglove.Ros2Bridge.asmdef",
                "Editor/Unity2Foxglove.Ros2Bridge.Editor.asmdef",
                "Tests/Unity2Foxglove.Ros2Bridge.Tests.asmdef",
                "Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity",
                "Samples~/Ros2BridgeSample/Scripts/Unity2Foxglove.Ros2Bridge.Sample.asmdef",
                "Editor/SourceGenerators/analyzers/dotnet/cs/Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll",
            )
            for relative in old_required:
                asset = package / relative
                asset.parent.mkdir(parents=True, exist_ok=True)
                asset.write_bytes(b"{}")
                Path(str(asset) + ".meta").write_text("guid: test\n", encoding="utf-8")

            self.validator.ROS2_BRIDGE_PACKAGE = package
            results = []
            self.validator.check_ros2_bridge_package(results)

        required = next(
            item for item in results
            if item.name == "ROS2 Bridge required assets and metas"
        )
        self.assertFalse(required.ok)
        self.assertIn("Ros2BridgeSampleDuplex.cs", required.detail)
        self.assertIn("Ros2BridgeSampleSceneBuilder.cs", required.detail)
        self.assertIn("PHASE186_BREAKING_UPGRADE.md", required.detail)

    def test_optional_package_boundaries_reject_remote_gateway_publish_sentinel(self) -> None:
        """Preview native packages should not carry stale publish-blocker sentinel text."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            gateway = root / "Packages" / "dev.unity2foxglove.remotegateway.win64"
            gateway.mkdir(parents=True)
            (gateway / "THIRD_PARTY_NOTICES.md").write_text(
                "Before publishing this package, regenerate notices.\n",
                encoding="utf-8",
            )

            self.validator.REMOTE_GATEWAY_PACKAGE = gateway
            self.validator.ROS2_RUNTIME_PACKAGES = ()
            self.validator.UNITY_DEMO_ASSETS = root / "Assets"
            results = []
            self.validator.check_optional_package_boundaries(results)

        self.assertFalse(results[0].ok)

    def test_optional_package_boundaries_require_ros2_runtime_conflicts(self) -> None:
        """Sibling ROS2 runtime packages share one assembly name and must declare conflicts."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            humble = root / "Packages" / "humble"
            jazzy = root / "Packages" / "jazzy"
            humble.mkdir(parents=True)
            jazzy.mkdir(parents=True)
            (humble / "package.json").write_text(
                '{"name":"humble","unity2foxgloveConflicts":["jazzy"]}',
                encoding="utf-8",
            )
            (jazzy / "package.json").write_text(
                '{"name":"jazzy"}',
                encoding="utf-8",
            )

            self.validator.REMOTE_GATEWAY_PACKAGE = root / "missing"
            self.validator.ROS2_RUNTIME_PACKAGES = (humble, jazzy)
            self.validator.UNITY_DEMO_ASSETS = root / "Assets"
            results = []
            self.validator.check_optional_package_boundaries(results)

        conflict_result = next(item for item in results if item.name == "ROS2 runtime package conflict metadata")
        self.assertFalse(conflict_result.ok)

    def test_optional_package_boundaries_reject_duplicate_demo_link_xml(self) -> None:
        """The demo project should rely on the package link.xml instead of a copied asset."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            assets = root / "Assets"
            assets.mkdir()
            (assets / "link.xml").write_text("<linker />", encoding="utf-8")

            self.validator.REMOTE_GATEWAY_PACKAGE = root / "missing"
            self.validator.ROS2_RUNTIME_PACKAGES = ()
            self.validator.UNITY_DEMO_ASSETS = assets
            results = []
            self.validator.check_optional_package_boundaries(results)

        link_result = next(item for item in results if item.name == "demo project avoids duplicate package link.xml")
        self.assertFalse(link_result.ok)


__all__ = ["PackageStructureTests"]
