from __future__ import annotations

from .foundation import *


class Phase186PackageManifestTests:
    """Exercise package identity and dependency matrix validation."""

    def test_public_boundary_authenticates_the_complete_package_matrix(self) -> None:
        """The public matrix gate rejects identity, version, and dependency drift."""

        expected_packages = {
            "sdk": (
                "dev.unity2foxglove.sdk",
                "1.9.7",
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
                {"dev.unity2foxglove.sdk": "1.9.7"},
            ),
            "bridge": (
                "dev.unity2foxglove.ros2bridge",
                "0.1.0-preview.1",
                {"dev.unity2foxglove.sdk": "1.9.7"},
            ),
            "remote_gateway": (
                "dev.unity2foxglove.remotegateway.win64",
                "0.1.0-preview.1",
                {"dev.unity2foxglove.sdk": "1.9.7"},
            ),
        }

        def write_manifest(path: Path, key: str) -> None:
            """Write the expected package manifest fixture selected by key."""
            name, version, dependencies = expected_packages[key]
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                json.dumps(
                    {
                        "name": name,
                        "version": version,
                        "dependencies": dependencies,
                    }
                ),
                encoding="utf-8",
            )

        def write_analyzer_assets(package: Path, dll_name: str, project_name: str) -> None:
            """Write analyzer DLL and project fixtures for the package matrix."""
            analyzer = package / "Editor/SourceGenerators/analyzers/dotnet/cs" / dll_name
            analyzer.parent.mkdir(parents=True, exist_ok=True)
            analyzer.write_bytes(b"dll")
            analyzer_guid = {
                "dev.unity2foxglove.sdk": "a" * 32,
                "dev.unity2foxglove.ros2forunity": "b" * 32,
                "dev.unity2foxglove.ros2bridge": "c" * 32,
            }[package.name]
            Path(str(analyzer) + ".meta").write_text(
                "fileFormatVersion: 2\nguid: "
                + analyzer_guid
                + "\nlabels:\n- RoslynAnalyzer\nPluginImporter:\n",
                encoding="utf-8",
            )
            (package / "Editor/SourceGenerators" / project_name).write_text(
                "<Project />\n", encoding="utf-8"
            )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            packages_root = root / "Packages"
            package_paths = {
                key: packages_root / relative
                for key, relative in {
                    "sdk": "dev.unity2foxglove.sdk",
                    "r2fu": "dev.unity2foxglove.ros2forunity",
                    "bridge": "dev.unity2foxglove.ros2bridge",
                    "remote_gateway": "dev.unity2foxglove.remotegateway.win64",
                }.items()
            }
            for key, package in package_paths.items():
                package.mkdir(parents=True)
                write_manifest(package / "package.json", key)

            for key, dll_name, project_name in (
                ("sdk", "FoxgloveLogSourceGenerator.dll", "FoxgloveLogSourceGenerator.csproj"),
                ("r2fu", "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll", "FoxRunR2fuSourceGenerator.csproj"),
                ("bridge", "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll", "FoxRunBridgeSourceGenerator.csproj"),
            ):
                write_analyzer_assets(package_paths[key], dll_name, project_name)

            mutations = (
                (
                    "bridge dependency value",
                    "bridge",
                    lambda data: data["dependencies"].update({"dev.unity2foxglove.sdk": "9.9.9"}),
                    r"package matrix bridge dependencies",
                ),
                (
                    "bridge unexpected dependency",
                    "bridge",
                    lambda data: data["dependencies"].update({"com.example.unexpected": "1.0.0"}),
                    r"package matrix bridge dependencies",
                ),
                (
                    "r2fu identity",
                    "r2fu",
                    lambda data: data.update({"name": "dev.unity2foxglove.ros2forunity.moved"}),
                    r"package matrix r2fu name",
                ),
                (
                    "r2fu dependency missing",
                    "r2fu",
                    lambda data: data["dependencies"].pop("dev.unity2foxglove.sdk"),
                    r"package matrix r2fu dependencies",
                ),
                (
                    "r2fu dependency value",
                    "r2fu",
                    lambda data: data["dependencies"].update({"dev.unity2foxglove.sdk": "9.9.9"}),
                    r"package matrix r2fu dependencies",
                ),
                (
                    "remote gateway version",
                    "remote_gateway",
                    lambda data: data.update({"version": "9.9.9"}),
                    r"package matrix remote_gateway version",
                ),
                (
                    "remote gateway unexpected dependency",
                    "remote_gateway",
                    lambda data: data["dependencies"].update({"com.example.unexpected": "1.0.0"}),
                    r"package matrix remote_gateway dependencies",
                ),
                (
                    "sdk dependency set",
                    "sdk",
                    lambda data: data["dependencies"].update({"com.unity.burst": "9.9.9"}),
                    r"package matrix sdk dependencies",
                ),
                (
                    "sdk dependency missing",
                    "sdk",
                    lambda data: data["dependencies"].pop("com.unity.collections"),
                    r"package matrix sdk dependencies",
                ),
                (
                    "sdk unexpected dependency",
                    "sdk",
                    lambda data: data["dependencies"].update({"com.example.unexpected": "1.0.0"}),
                    r"package matrix sdk dependencies",
                ),
                (
                    "missing remote gateway manifest",
                    "remote_gateway",
                    lambda data: None,
                    r"package matrix remote_gateway manifest",
                ),
            )
            with mock.patch.object(self.validator, "ROOT", root):
                for label, key, mutate, expected_error in mutations:
                    with self.subTest(label=label):
                        manifest = package_paths[key] / "package.json"
                        if label == "missing remote gateway manifest":
                            manifest.unlink()
                        else:
                            data = json.loads(manifest.read_text(encoding="utf-8"))
                            mutate(data)
                            manifest.write_text(json.dumps(data), encoding="utf-8")
                        with self.assertRaisesRegex(RuntimeError, expected_error):
                            self.validator.validate_boundaries()
                        if not manifest.exists():
                            write_manifest(manifest, key)
                        else:
                            write_manifest(manifest, key)


__all__ = ["Phase186PackageManifestTests"]
