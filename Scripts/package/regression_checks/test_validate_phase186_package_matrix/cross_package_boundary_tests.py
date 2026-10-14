from __future__ import annotations

from .foundation import *


class Phase186CrossPackageBoundaryTests:
    """Exercise sibling package assembly-boundary resolution."""

    def test_public_boundary_rejects_sibling_packaged_r2fu_guid_reference(self) -> None:
        """The public matrix gate resolves R2FU child GUIDs across package roots."""

        def write_asmdef(
            path: Path,
            name: str,
            guid: str,
            references: list[str] | None = None,
        ) -> None:
            """Write a temporary assembly definition and its Unity GUID metadata."""
            payload = {"name": name}
            if references is not None:
                payload["references"] = references
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(payload), encoding="utf-8")
            Path(str(path) + ".meta").write_text(
                f"fileFormatVersion: 2\nguid: {guid}\n",
                encoding="utf-8",
            )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            packages = root / "Packages"
            sdk = packages / "dev.unity2foxglove.sdk"
            r2fu = packages / "dev.unity2foxglove.ros2forunity"
            bridge = packages / "dev.unity2foxglove.ros2bridge"
            gateway = packages / "dev.unity2foxglove.remotegateway.win64"
            sibling = packages / "dev.unity2foxglove.ros2forunity.runtime.humble.win64"
            unrelated = packages / "unrelated.package"
            for package in (sdk, r2fu, bridge, gateway, sibling, unrelated):
                package.mkdir(parents=True)

            (sdk / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.sdk",
                        "version": "1.9.6",
                        "dependencies": {
                            "com.unity.nuget.newtonsoft-json": "3.2.1",
                            "com.unity.burst": "1.8.18",
                            "com.unity.collections": "2.5.5",
                            "com.unity.mathematics": "1.3.2",
                        },
                    }
                ),
                encoding="utf-8",
            )
            (r2fu / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.ros2forunity",
                        "version": "0.1.0-preview.1",
                        "dependencies": {"dev.unity2foxglove.sdk": "1.9.6"},
                    }
                ),
                encoding="utf-8",
            )
            (bridge / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.ros2bridge",
                        "version": "0.1.0-preview.1",
                        "dependencies": {"dev.unity2foxglove.sdk": "1.9.6"},
                    }
                ),
                encoding="utf-8",
            )
            (gateway / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.remotegateway.win64",
                        "version": "0.1.0-preview.1",
                        "dependencies": {"dev.unity2foxglove.sdk": "1.9.6"},
                    }
                ),
                encoding="utf-8",
            )
            (sibling / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
                        "version": "0.1.0-preview.1",
                        "dependencies": {},
                    }
                ),
                encoding="utf-8",
            )
            (unrelated / "package.json").write_text(
                json.dumps({"name": "unrelated.package", "version": "1.0.0"}),
                encoding="utf-8",
            )

            write_asmdef(
                r2fu / "Runtime.asmdef",
                "Unity2Foxglove.Ros2ForUnity",
                "11111111111111111111111111111111",
            )
            write_asmdef(
                r2fu / "Editor.asmdef",
                "Unity2Foxglove.Ros2ForUnity.Editor",
                "55555555555555555555555555555555",
            )
            write_asmdef(
                sibling / "RuntimeChild.asmdef",
                "Unity2Foxglove.Ros2ForUnity.Runtime",
                "22222222222222222222222222222222",
            )
            write_asmdef(
                unrelated / "Other.asmdef",
                "Unity2Foxglove.Ros2ForUnity.Decoy",
                "44444444444444444444444444444444",
            )
            write_asmdef(
                sdk / "Sdk.asmdef",
                "Unity2Foxglove.Sdk",
                "66666666666666666666666666666666",
            )

            for package, dll_name, project_name in (
                (sdk, "FoxgloveLogSourceGenerator.dll", "FoxgloveLogSourceGenerator.csproj"),
                (r2fu, "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll", "FoxRunR2fuSourceGenerator.csproj"),
                (bridge, "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll", "FoxRunBridgeSourceGenerator.csproj"),
            ):
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

            bridge_descriptor = bridge / "Bridge.asmdef"
            cases = (
                (
                    "sibling GUID",
                    ["GUID:22222222222222222222222222222222"],
                    True,
                ),
                (
                    "sibling name",
                    ["Unity2Foxglove.Ros2ForUnity.Runtime"],
                    True,
                ),
                (
                    "unrelated GUID",
                    ["GUID:44444444444444444444444444444444"],
                    False,
                ),
                (
                    "co-located child GUID",
                    ["GUID:55555555555555555555555555555555"],
                    True,
                ),
                ("clean", [], False),
            )
            with mock.patch.object(self.validator, "ROOT", root):
                for label, references, should_fail in cases:
                    with self.subTest(label=label):
                        write_asmdef(
                            bridge_descriptor,
                            "Bridge",
                            "33333333333333333333333333333333",
                            references,
                        )
                        if should_fail:
                            with self.assertRaisesRegex(
                                RuntimeError,
                                r"Bridge\.asmdef references Unity2Foxglove\.Ros2ForUnity",
                            ):
                                self.validator.validate_boundaries()
                        else:
                            checked = self.validator.validate_boundaries()
                            self.assertIn("Packages/dev.unity2foxglove.ros2bridge/Bridge.asmdef", checked)

                write_asmdef(
                    sdk / "Sdk.asmdef",
                    "Unity2Foxglove.Sdk",
                    "66666666666666666666666666666666",
                    ["Unity2Foxglove.Ros2ForUnity"],
                )
                with self.assertRaisesRegex(
                    RuntimeError,
                    r"Sdk\.asmdef references Unity2Foxglove\.Ros2ForUnity",
                ):
                    self.validator.validate_boundaries()


__all__ = ["Phase186CrossPackageBoundaryTests"]
