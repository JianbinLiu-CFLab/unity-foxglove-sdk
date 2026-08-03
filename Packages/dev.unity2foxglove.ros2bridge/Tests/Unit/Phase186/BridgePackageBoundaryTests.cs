// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests.Unit.Phase186
{
    public sealed class BridgePackageBoundaryTests
    {
        [Fact]
        public void BridgeOwnsIndependentAnalyzerAndPhysicalContribution()
        {
            var root = FindRepositoryRoot();
            var generatorRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Editor",
                "SourceGenerators");
            var project = File.ReadAllText(Path.Combine(
                generatorRoot,
                "FoxRunBridgeSourceGenerator.csproj"));
            var contribution = File.ReadAllText(Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Editor",
                "FoxRun",
                "FoxRunBridgeEmitterContribution.cs"));
            var analyzer = Path.Combine(
                generatorRoot,
                "analyzers",
                "dotnet",
                "cs",
                "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll");

            Assert.Contains(
                "FOXRUN_BRIDGE_ANALYZER",
                project,
                StringComparison.Ordinal);
            Assert.Contains(
                "<AssemblyName>Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator</AssemblyName>",
                project,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "dev.unity2foxglove.ros2forunity",
                project,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                @"FoxgloveSourceEmitter\**\*.cs",
                project,
                StringComparison.Ordinal);
            Assert.True(File.Exists(analyzer), analyzer);
            Assert.True(File.Exists(analyzer + ".meta"), analyzer + ".meta");
            Assert.Contains(
                "HintNameSuffix => \"typed-cdr\"",
                contribution,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunBridgeSourceEmitter.EmitBridgeContribution",
                contribution,
                StringComparison.Ordinal);
        }

        [Fact]
        public void BridgeAnalyzerIgnoresDefaultTransportMembers()
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9);
            const string source = @"
using Unity.FoxgloveSDK.Components;
namespace Demo
{
    public partial class DefaultPublisher
    {
        [FoxRun(""/phase186/default"")]
        private int _value;
    }
}";
            var compilation = CSharpCompilation.Create(
                "phase186_bridge_default_transport",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                PlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                FoxRunAnalyzerTestComposition.CoreAndBridge(),
                parseOptions: parseOptions);
            driver = driver.RunGenerators(compilation);
            var run = driver.GetRunResult();

            Assert.All(
                run.Results,
                result => Assert.Null(result.Exception));
            Assert.Empty(run.Diagnostics.Where(
                diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error));
            Assert.DoesNotContain(
                run.Results.SelectMany(
                    result => result.GeneratedSources),
                item => item.HintName.Contains(
                    "ros2bridge",
                    StringComparison.Ordinal));
        }

        [Fact]
        public void CoreAndBridgeAnalyzersEmitDistinctPartials()
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9);
            const string source = @"
using Unity.FoxgloveSDK.Components;
namespace Demo
{
    public sealed class State
    {
        public State() { }
        public int Count;
        public string Label;
    }

    public partial class Publisher
    {
        [FoxRun(""/phase186/bridge"",
            PublishTransportIds = new[] { ""unity2foxglove.ros2bridge"" })]
        private State _state = new State();
    }
}";
            var compilation = CSharpCompilation.Create(
                "phase186_bridge_analyzer_composition",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                PlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                FoxRunAnalyzerTestComposition.CoreAndBridge(),
                parseOptions: parseOptions);
            var run = driver.RunGenerators(compilation).GetRunResult();
            var generated = run.Results
                .SelectMany(result => result.GeneratedSources)
                .ToDictionary(
                    item => item.HintName,
                    item => item.SourceText.ToString(),
                    StringComparer.Ordinal);

            Assert.Empty(run.Diagnostics.Where(
                diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error));
            Assert.Contains(
                "Demo_Publisher_FoxRun.g.cs",
                generated.Keys);
            Assert.Contains(
                "__foxRunCaptureSequence_0",
                generated["Demo_Publisher_FoxRun.g.cs"],
                StringComparison.Ordinal);
            var bridgeHint =
                "Demo_Publisher_unity2foxglove_ros2bridge_typed_cdr_FoxRun.g.cs";
            Assert.Contains(bridgeHint, generated.Keys);
            Assert.Contains(
                "__TryBuildFoxRunRos2Cdr_0",
                generated[bridgeHint],
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunBridgeCustomDtoBudgetPolicy.MaximumBytes",
                generated[bridgeHint],
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Ros2ForUnity",
                generated[bridgeHint],
                StringComparison.Ordinal);
            Assert.Equal(
                generated.Count,
                generated.Keys.Distinct(StringComparer.Ordinal).Count());
        }

        private static MetadataReference[] PlatformReferences()
        {
            var trusted = (string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES");
            return trusted
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Concat(new[] { typeof(FoxRunAttribute).Assembly.Location })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(
                        current.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Unity2Foxglove repository root.");
        }
    }
}
