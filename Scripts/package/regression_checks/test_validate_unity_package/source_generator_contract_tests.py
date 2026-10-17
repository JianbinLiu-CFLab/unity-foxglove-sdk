from __future__ import annotations

from .foundation import *


class SourceGeneratorContractTests:
    """Exercise source-generator command and composition contracts."""

    def test_build_failure_returns_structured_failure(self) -> None:
        """A failed dotnet build should not surface as a Python traceback."""
        failed = subprocess.CalledProcessError(returncode=9, cmd=["dotnet", "build"])
        with mock.patch.object(self.validator.subprocess, "run", side_effect=failed):
            with mock.patch("sys.stderr") as stderr:
                self.assertFalse(self.validator.run_build())

        written = "".join(call.args[0] for call in stderr.write.call_args_list if call.args)
        self.assertIn("[FAIL] Source generator Release build failed", written)

    def test_missing_dotnet_returns_structured_build_failure(self) -> None:
        """A missing dotnet executable should not surface as a traceback."""
        with mock.patch.object(
            self.validator.subprocess,
            "run",
            side_effect=FileNotFoundError("dotnet"),
        ):
            with mock.patch("sys.stderr") as stderr:
                self.assertFalse(self.validator.run_build())

        written = "".join(
            call.args[0] for call in stderr.write.call_args_list if call.args
        )
        self.assertIn("dotnet executable is unavailable", written)

    def test_unmatched_compile_glob_fails_closed(self) -> None:
        """A typo in a controlled Compile glob must not erase its source set."""
        with tempfile.TemporaryDirectory() as temp:
            project = Path(temp) / "Generator.csproj"
            project.write_text(
                '<Project><ItemGroup><Compile Include="src/**/*.missing.cs" />'
                "</ItemGroup></Project>",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "matched no files"):
                self.validator._project_sources(project)

    def test_composition_contract_rejects_commented_and_skipped_tests(self) -> None:
        """Comments and skipped facts cannot satisfy analyzer composition coverage."""
        source = """
// CoreOnly() R2fuOnly() BridgeOnly() AllProviders()
public static IEnumerable<object[]> AnalyzerSets() { yield break; }
[Theory(Skip = "disabled")]
[MemberData(nameof(AnalyzerSets))]
public void IndependentAnalyzerSetsEmitOnlyOwnedUniqueHints() { }
[Fact(Skip = "disabled")]
public void PhysicalAndRoslynProviderEmittersStayEquivalent() { }
"""
        failures = self.validator._composition_contract_failures(source)
        self.assertIn("active analyzer-set calls", failures)
        self.assertIn("active analyzer-set theory", failures)
        self.assertIn("active physical/Roslyn parity fact", failures)

    def test_composition_tests_receive_cli_msbuild_properties(self) -> None:
        """The nested composition lane must keep the caller's isolated output roots."""
        properties = [
            "-p:BaseOutputPath=C:/ci/analyzer/bin/",
            "-p:BaseIntermediateOutputPath=C:/ci/analyzer/obj/",
        ]
        argv = [
            "validate_source_generator_dll.py",
            *("--msbuild-prop=" + prop for prop in properties),
        ]

        with mock.patch.object(
            self.validator,
            "validate_analyzer_contracts",
            return_value=True,
        ):
            with mock.patch.object(
                self.validator,
                "validate_or_update",
                return_value=0,
            ):
                with mock.patch.object(self.validator.subprocess, "run") as run:
                    with mock.patch("sys.argv", argv):
                        self.assertEqual(0, self.validator.main())

        command = run.call_args.args[0]
        for prop in properties:
            self.assertIn(prop, command)

    def test_msbuild_compile_include_normalizes_windows_separators(self) -> None:
        """MSBuild Include paths must resolve identically on Windows and POSIX hosts."""
        self.assertEqual(
            "src/Shared/FoxRunGenerationModel.cs",
            self.validator._normalize_compile_include(
                r"src\Shared\FoxRunGenerationModel.cs"
            ),
        )

    def test_provider_analyzers_use_owned_explicit_sources_and_roslyn_only_dependencies(self) -> None:
        """Provider analyzers must not compile core trees or carry runtime codec dependencies."""
        projects = (
            ROOT
            / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/FoxRunR2fuSourceGenerator.csproj",
            ROOT
            / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/FoxRunBridgeSourceGenerator.csproj",
        )
        for project in projects:
            package_root = project.parents[2].resolve()
            root = ET.parse(project).getroot()
            dependencies = {
                node.attrib["Include"]
                for node in root.findall(".//PackageReference")
            }
            self.assertEqual(
                {
                    "Microsoft.CodeAnalysis.Analyzers",
                    "Microsoft.CodeAnalysis.CSharp",
                },
                dependencies,
                project,
            )
            self.assertFalse(root.findall(".//ProjectReference"), project)
            self.assertFalse(root.findall(".//Reference"), project)
            for node in root.findall(".//Compile"):
                for include in node.attrib.get("Include", "").split(";"):
                    include = include.strip()
                    if not include:
                        continue
                    self.assertNotIn("*", include, project)
                    source = (project.parent / include).resolve()
                    self.assertTrue(
                        source.is_relative_to(package_root),
                        f"{project}: non-owned source {include}",
                    )


__all__ = ["SourceGeneratorContractTests"]
