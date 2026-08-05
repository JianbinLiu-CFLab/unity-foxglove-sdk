// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Phase186A R2FU Provider identity, ownership, and lifecycle contract.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.Ros2ForUnity
{
    public sealed class FoxRunR2fuTransportProviderTests
    {
        [Fact]
        public void ProviderExposesTheLockedIdAndDuplexCapabilities()
        {
            var provider = new FoxRunRos2TransportProvider();

            Assert.Equal("unity2foxglove.r2fu", FoxRunRos2TransportProvider.IdValue);
            Assert.Equal(new FoxRunTransportId("unity2foxglove.r2fu"), provider.Id);
            Assert.Equal(
                FoxRunTransportCapabilities.Publish | FoxRunTransportCapabilities.Subscribe,
                provider.Capabilities);
        }

        [Fact]
        public void ProviderIsHiddenSerializedAndCannotBeDuplicated()
        {
            var type = typeof(FoxRunRos2TransportProvider);

            Assert.True(type.IsSealed);
            Assert.NotNull(type.GetCustomAttribute<DisallowMultipleComponentAttribute>());
            var menu = type.GetCustomAttribute<AddComponentMenuAttribute>();
            Assert.NotNull(menu);
        }

        [Fact]
        public void ProviderOwnsManagerLocalHubsWithoutGlobalBootstrapObjects()
        {
            var root = FindRepositoryRoot();
            var provider = File.ReadAllText(Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun",
                "FoxRunRos2TransportProvider.cs"));
            var publisherHub = File.ReadAllText(Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun",
                "FoxRunRos2CustomPublisherHub.cs"));
            var subscriptionHub = File.ReadAllText(Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun",
                "FoxRunRos2SubscriptionHub.cs"));

            Assert.Contains("GetComponent<FoxgloveManager>()", provider, StringComparison.Ordinal);
            Assert.Contains("RegisterFoxRunTransportProvider(this)", provider, StringComparison.Ordinal);
            Assert.Contains("GetOrAddOwnedHub<FoxRunRos2CustomPublisherHub>()", provider, StringComparison.Ordinal);
            Assert.Contains("GetOrAddOwnedHub<FoxRunRos2SubscriptionHub>()", provider, StringComparison.Ordinal);
            Assert.DoesNotContain("RuntimeInitializeOnLoadMethod", publisherHub, StringComparison.Ordinal);
            Assert.DoesNotContain("RuntimeInitializeOnLoadMethod", subscriptionHub, StringComparison.Ordinal);
            Assert.DoesNotContain("HideAndDontSave", publisherHub, StringComparison.Ordinal);
            Assert.DoesNotContain("HideAndDontSave", subscriptionHub, StringComparison.Ordinal);
        }

        [Fact]
        public void R2fuPackageDoesNotReferenceTheBridgePackage()
        {
            var root = FindRepositoryRoot();
            var asmdefs = Directory.GetFiles(
                Path.Combine(
                    root,
                    "Packages",
                    "dev.unity2foxglove.ros2forunity"),
                "*.asmdef",
                SearchOption.AllDirectories);

            Assert.NotEmpty(asmdefs);
            Assert.All(
                asmdefs,
                path => Assert.DoesNotContain(
                    "Ros2Bridge",
                    File.ReadAllText(path),
                    StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void R2fuOwnsAnIndependentControlledAnalyzerAndPhysicalContribution()
        {
            var root = FindRepositoryRoot();
            var generatorRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Editor",
                "SourceGenerators");
            var project = File.ReadAllText(Path.Combine(
                generatorRoot,
                "FoxRunR2fuSourceGenerator.csproj"));
            var sharedScanner = File.ReadAllText(Path.Combine(
                generatorRoot,
                "src",
                "Shared",
                "FoxgloveLogSourceGenerator.cs"));
            var providerPipeline = File.ReadAllText(Path.Combine(
                generatorRoot,
                "src",
                "FoxRunR2fuAnalyzerPipeline.cs"));
            var releaseLedger = File.ReadAllText(Path.Combine(
                generatorRoot,
                "AnalyzerReleases.Unshipped.md"));
            var contribution = File.ReadAllText(Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Editor",
                "Native",
                "FoxRunR2fuEmitterContribution.cs"));
            var analyzer = Path.Combine(
                generatorRoot,
                "analyzers",
                "dotnet",
                "cs",
                "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll");

            Assert.Contains("FOXRUN_R2FU_ANALYZER", project, StringComparison.Ordinal);
            Assert.Contains("FOXRUN_PROVIDER_ANALYZER", project, StringComparison.Ordinal);
            Assert.Contains(
                "<AssemblyName>Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator</AssemblyName>",
                project,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Ros2Bridge", project, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Google.Protobuf", project, StringComparison.Ordinal);
            Assert.DoesNotContain("src\\Legacy", project, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("src\\Shared\\FoxgloveLogSourceGenerator.cs", project, StringComparison.Ordinal);
            Assert.Contains("src\\FoxRunR2fuAnalyzerPipeline.cs", project, StringComparison.Ordinal);
            Assert.True(File.Exists(analyzer), analyzer);
            Assert.True(File.Exists(analyzer + ".meta"), analyzer + ".meta");
            Assert.Contains("#if FOXRUN_PROVIDER_ANALYZER", sharedScanner, StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunProviderAnalyzer.Register(context, members)",
                sharedScanner,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunR2fuAnalyzerEmitter.Emit",
                providerPipeline,
                StringComparison.Ordinal);
            Assert.Contains(
                "_unity2foxglove_r2fu_typed_ros2_FoxRun.g.cs",
                providerPipeline,
                StringComparison.Ordinal);
            Assert.All(
                Enumerable.Range(1, 16),
                number => Assert.Contains(
                    "FOXR2F" + number.ToString("000"),
                    releaseLedger,
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                "FOXRUN",
                releaseLedger,
                StringComparison.Ordinal);
            Assert.Contains("FoxRunR2fuSourceEmitter.Emit", contribution, StringComparison.Ordinal);
            Assert.Contains("HintNameSuffix => \"typed-ros2\"", contribution, StringComparison.Ordinal);
        }

        [Fact]
        public void CoreAndR2fuAnalyzersEmitDistinctNonOverlappingPartials()
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols:
                    new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var source = @"
using Unity.FoxgloveSDK.Components;
namespace vendor_msgs.msg
{
    public sealed class Command : ROS2.Message
    {
        public Command() { }
        public int Value;
    }
}
namespace Demo
{
    public sealed class State
    {
        public int Value { get; set; }
    }

    public partial class Receiver
    {
        [FoxRun(""/phase186/r2fu"",
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId = ""unity2foxglove.r2fu"",
            SchemaName = ""vendor_msgs/msg/Command"")]
        private vendor_msgs.msg.Command _incoming;

        [FoxRun(""/phase186/r2fu/custom"",
            Mode = FoxRunFlow.PublishAndSubscribe,
            PublishTransportIds = new[] { ""unity2foxglove.r2fu"" },
            SubscribeTransportId = ""unity2foxglove.r2fu"")]
        private State _state;
    }
}";
            var references = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Concat(new[]
                {
                    typeof(FoxRunAttribute).Assembly.Location,
                    typeof(FoxRunRos2TransportProvider).Assembly.Location
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
            var compilation = CSharpCompilation.Create(
                "phase186_r2fu_analyzer_composition",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.CoreAndR2fu(),
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
            Assert.Contains("Demo_Receiver_FoxRun.g.cs", generated.Keys);
            Assert.Contains(
                "Demo_Receiver_unity2foxglove_r2fu_typed_ros2_FoxRun.g.cs",
                generated.Keys);
            Assert.DoesNotContain(
                "IFoxRunRos2SubscriptionSource",
                generated["Demo_Receiver_FoxRun.g.cs"],
                StringComparison.Ordinal);
            Assert.Contains(
                "IFoxRunRos2SubscriptionSource",
                generated[
                    "Demo_Receiver_unity2foxglove_r2fu_typed_ros2_FoxRun.g.cs"],
                StringComparison.Ordinal);
            Assert.Contains(
                "IFoxRunRos2CustomSubscriptionSource",
                generated[
                    "Demo_Receiver_unity2foxglove_r2fu_typed_ros2_FoxRun.g.cs"],
                StringComparison.Ordinal);
            Assert.Contains(
                "IFoxRunRos2CustomPublisherSource",
                generated[
                    "Demo_Receiver_unity2foxglove_r2fu_typed_ros2_FoxRun.g.cs"],
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "IFoxRunRos2CustomSubscriptionSource",
                generated["Demo_Receiver_FoxRun.g.cs"],
                StringComparison.Ordinal);
            Assert.Equal(
                generated.Count,
                run.Results
                    .SelectMany(result => result.GeneratedSources)
                    .Select(item => item.HintName)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
        }

        [Fact]
        public void R2fuAnalyzerPreservesNeutralDeliveryPolicyEnumValues()
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols:
                    new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var source = @"
using Unity.FoxgloveSDK.Components;
namespace vendor_msgs.msg
{
    public sealed class Command : ROS2.Message
    {
        public Command() { }
        public int Value;
    }
}
namespace Demo
{
    public partial class Receiver
    {
        [FoxRun(""/phase186/r2fu/qos"",
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId = ""unity2foxglove.r2fu"",
            Reliability = FoxRunDeliveryReliability.Reliable,
            Durability = FoxRunDeliveryDurability.TransientLocal,
            History = FoxRunDeliveryHistory.KeepLast,
            Depth = 7)]
        private vendor_msgs.msg.Command _incoming;
    }
}";
            var references = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Concat(new[]
                {
                    typeof(FoxRunAttribute).Assembly.Location,
                    typeof(FoxRunRos2TransportProvider).Assembly.Location
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
            var compilation = CSharpCompilation.Create(
                "phase186_r2fu_neutral_qos",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.R2fuOnly(),
                parseOptions: parseOptions);
            var run = driver.RunGenerators(compilation).GetRunResult();
            var errors = run.Diagnostics
                .Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.Empty(errors);
            var generated = Assert.Single(
                    run.Results.SelectMany(result => result.GeneratedSources))
                .SourceText
                .ToString();
            Assert.Contains(
                "FoxRunQosReliability.Reliable",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunQosDurability.TransientLocal",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunQosHistory.KeepLast",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "                7,",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        public void R2fuAnalyzerReportsProviderOwnedShapeDiagnostics()
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols:
                    new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var source = @"
using Unity.FoxgloveSDK.Components;
namespace invalid_msgs.msg
{
    public sealed class Recursive : ROS2.Message
    {
        public Recursive() { }
        public Recursive Next;
    }
}
namespace Demo
{
    public partial class Receiver
    {
        [FoxRun(""/phase186/r2fu/invalid"",
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId = ""unity2foxglove.r2fu"")]
        private invalid_msgs.msg.Recursive _incoming;
    }
}";
            var references = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Concat(new[]
                {
                    typeof(FoxRunAttribute).Assembly.Location,
                    typeof(FoxRunRos2TransportProvider).Assembly.Location
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
            var compilation = CSharpCompilation.Create(
                "phase186_r2fu_diagnostic_ownership",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.R2fuOnly(),
                parseOptions: parseOptions);
            var run = driver.RunGenerators(compilation).GetRunResult();
            var diagnostics = run.Diagnostics
                .Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Id == "FOXR2F006");
            Assert.DoesNotContain(
                diagnostics,
                diagnostic => diagnostic.Id.StartsWith(
                    "FOXRUN",
                    StringComparison.Ordinal));
        }

        [Fact]
        public void RejectedDtoMemberDoesNotReserveItsRosFieldName()
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols:
                    new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var source = @"
using Unity.FoxgloveSDK.Components;
namespace Demo
{
    public sealed class State
    {
        public int Foo { get; }
        public int foo;
    }

    public partial class Receiver
    {
        [FoxRun(""/phase187/r2fu/diagnostics"",
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId = ""unity2foxglove.r2fu"")]
        private State _incoming;
    }
}";
            var references = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Concat(new[]
                {
                    typeof(FoxRunAttribute).Assembly.Location,
                    typeof(FoxRunRos2TransportProvider).Assembly.Location
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
            var compilation = CSharpCompilation.Create(
                "phase187_r2fu_diagnostic_reservation",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.R2fuOnly(),
                parseOptions: parseOptions);
            var run = driver.RunGenerators(compilation).GetRunResult();
            var diagnostics = run.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.Single(diagnostics);
            Assert.Equal("FOXR2F011", diagnostics[0].Id);
            Assert.DoesNotContain(
                "collides",
                diagnostics[0].GetMessage(),
                StringComparison.OrdinalIgnoreCase);
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
#endif
