from __future__ import annotations

from .foundation import *


class Phase186AnalyzerMetadataTests:
    """Exercise analyzer metadata identity and importer validation."""

    def test_public_boundary_rejects_malformed_analyzer_meta(self) -> None:
        """The package matrix must authenticate analyzer GUID/importer metadata."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            packages = root / "Packages"
            roots = {
                key: packages / name
                for key, name in {
                    "sdk": "dev.unity2foxglove.sdk",
                    "r2fu": "dev.unity2foxglove.ros2forunity",
                    "bridge": "dev.unity2foxglove.ros2bridge",
                }.items()
            }
            for package in roots.values():
                package.mkdir(parents=True)
            for package in roots.values():
                (package / "package.json").write_text(
                    json.dumps({"name": package.name, "version": "1.0.0", "dependencies": {}}),
                    encoding="utf-8",
                )
            specs = (
                ("sdk", "FoxgloveLogSourceGenerator.dll", "FoxgloveLogSourceGenerator.csproj"),
                ("r2fu", "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll", "FoxRunR2fuSourceGenerator.csproj"),
                ("bridge", "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll", "FoxRunBridgeSourceGenerator.csproj"),
            )
            for key, dll_name, project_name in specs:
                analyzer = roots[key] / "Editor/SourceGenerators/analyzers/dotnet/cs" / dll_name
                analyzer.parent.mkdir(parents=True, exist_ok=True)
                analyzer.write_bytes(b"dll")
                guid = {"sdk": "1" * 32, "r2fu": "2" * 32, "bridge": "3" * 32}[key]
                meta = f"fileFormatVersion: 2\nguid: {guid}\nlabels:\n- RoslynAnalyzer\nPluginImporter:\n"
                Path(str(analyzer) + ".meta").write_text(meta, encoding="utf-8")
                (roots[key] / "Editor/SourceGenerators" / project_name).write_text("<Project />\n", encoding="utf-8")
            malformed = roots["sdk"] / "Editor/SourceGenerators/analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll.meta"
            malformed.write_text("fileFormatVersion: 2\nguid: malformed\nDefaultImporter:\n", encoding="utf-8")
            with mock.patch.object(self.validator, "ROOT", root), \
                mock.patch.object(self.validator, "validate_package_matrix", return_value={
                    "sdk": {"dependencies": {}}, "r2fu": {"dependencies": {}}, "bridge": {"dependencies": {}},
                }), \
                mock.patch.object(self.validator, "_package_matrix_paths", return_value=roots):
                with self.assertRaisesRegex(RuntimeError, "GUID is missing or malformed"):
                    self.validator.validate_boundaries()

    def test_public_boundary_rejects_analyzer_meta_identity_collisions(self) -> None:
        """Duplicate GUIDs and non-plugin importers must fail closed."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            packages = root / "Packages"
            roots = {
                key: packages / name
                for key, name in {
                    "sdk": "dev.unity2foxglove.sdk",
                    "r2fu": "dev.unity2foxglove.ros2forunity",
                    "bridge": "dev.unity2foxglove.ros2bridge",
                }.items()
            }
            specs = (
                ("sdk", "FoxgloveLogSourceGenerator.dll", "FoxgloveLogSourceGenerator.csproj"),
                ("r2fu", "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll", "FoxRunR2fuSourceGenerator.csproj"),
                ("bridge", "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll", "FoxRunBridgeSourceGenerator.csproj"),
            )
            metas: dict[str, Path] = {}
            for key, package in roots.items():
                package.mkdir(parents=True)
                (package / "package.json").write_text(
                    json.dumps({"name": package.name, "version": "1.0.0", "dependencies": {}}),
                    encoding="utf-8",
                )
            for key, dll_name, project_name in specs:
                analyzer = roots[key] / "Editor/SourceGenerators/analyzers/dotnet/cs" / dll_name
                analyzer.parent.mkdir(parents=True, exist_ok=True)
                analyzer.write_bytes(b"dll")
                meta = analyzer.with_name(analyzer.name + ".meta")
                metas[key] = meta
                guid = {"sdk": "1" * 32, "r2fu": "2" * 32, "bridge": "3" * 32}[key]
                meta.write_text(
                    f"fileFormatVersion: 2\nguid: {guid}\nlabels:\n- RoslynAnalyzer\nPluginImporter:\n",
                    encoding="utf-8",
                )
                (roots[key] / "Editor/SourceGenerators" / project_name).write_text("<Project />\n", encoding="utf-8")
            with mock.patch.object(self.validator, "ROOT", root), \
                mock.patch.object(self.validator, "validate_package_matrix", return_value={
                    "sdk": {"dependencies": {}}, "r2fu": {"dependencies": {}}, "bridge": {"dependencies": {}},
                }), \
                mock.patch.object(self.validator, "_package_matrix_paths", return_value=roots):
                metas["r2fu"].write_text(metas["sdk"].read_text(encoding="utf-8"), encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, "GUID duplicates"):
                    self.validator.validate_boundaries()
                metas["r2fu"].write_text(
                    "fileFormatVersion: 2\nguid: " + "2" * 32 + "\nDefaultImporter:\n",
                    encoding="utf-8",
                )
                with self.assertRaisesRegex(RuntimeError, "lacks PluginImporter"):
                    self.validator.validate_boundaries()


__all__ = ["Phase186AnalyzerMetadataTests"]
