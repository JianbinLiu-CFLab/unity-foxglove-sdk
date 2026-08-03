// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Lock all independently installable analyzer sets and emitter parity.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Unity2Foxglove.Ros2Bridge.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    public sealed class FoxRunAnalyzerCompositionContractTests
    {
        private const string Namespace =
            "AnalyzerCompositionFixtures";
        private const string ClassName = "CompositionHost";
        private const string CoreHint =
            "AnalyzerCompositionFixtures_CompositionHost_FoxRun.g.cs";
        private const string R2fuHint =
            "AnalyzerCompositionFixtures_CompositionHost_"
            + "unity2foxglove_r2fu_typed_ros2_FoxRun.g.cs";
        private const string BridgeHint =
            "AnalyzerCompositionFixtures_CompositionHost_"
            + "unity2foxglove_ros2bridge_typed_cdr_FoxRun.g.cs";

        private const string FixtureSource = @"
using Unity.FoxgloveSDK.Components;
namespace UnityEngine.Scripting
{
    [global::System.AttributeUsage(global::System.AttributeTargets.All)]
    public sealed class PreserveAttribute : global::System.Attribute
    {
    }
}
#if COMPOSITION_R2FU
namespace Unity2Foxglove.FoxRun.CustomRos2Typesupport
{
    public static class FoxRunRos2CustomTypesupportMetadata
    {
        public const int InterfaceRevision = 1;
        public const string InterfaceDigest = ""composition-digest"";
        public const string BaseRuntimePackageId = ""composition-runtime"";
    }
}
#endif
namespace AnalyzerCompositionFixtures
{
    public partial class CompositionHost
    {
        [FoxRun(""/phase186/composition/core"")]
        private int _core;

#if COMPOSITION_R2FU
        [FoxRun(""/phase186/composition/r2fu"",
            Mode = FoxRunFlow.PublishAndSubscribe,
            PublishTransportIds = new[] { ""unity2foxglove.r2fu"" },
            SubscribeTransportId = ""unity2foxglove.r2fu"")]
        private global::Unity.FoxgloveSDK.Tests.FoxRun.Fixtures.Phase181State _r2fu =
            new global::Unity.FoxgloveSDK.Tests.FoxRun.Fixtures.Phase181State();
#endif

        [FoxRun(""/phase186/composition/bridge"",
            Mode = FoxRunFlow.PublishAndSubscribe,
            PublishTransportIds = new[] { ""unity2foxglove.ros2bridge"" },
            SubscribeTransportId = ""unity2foxglove.ros2bridge"")]
        private global::Unity.FoxgloveSDK.Tests.FoxRun.Fixtures.Phase181State _bridge =
            new global::Unity.FoxgloveSDK.Tests.FoxRun.Fixtures.Phase181State();
    }
}";

        public static IEnumerable<object[]> AnalyzerSets()
        {
            yield return new object[]
            {
                "SDK-only",
                FoxRunAnalyzerTestComposition.CoreOnly(),
                new[] { CoreHint },
                false,
                false,
            };
            yield return new object[]
            {
                "R2FU-only",
                WithCore(
                    FoxRunAnalyzerTestComposition.R2fuOnly()),
                new[] { CoreHint, R2fuHint },
                true,
                false,
            };
            yield return new object[]
            {
                "Bridge-only",
                WithCore(
                    FoxRunAnalyzerTestComposition.BridgeOnly()),
                new[] { CoreHint, BridgeHint },
                false,
                true,
            };
            yield return new object[]
            {
                "all-installed",
                FoxRunAnalyzerTestComposition.AllProviders(),
                new[] { CoreHint, R2fuHint, BridgeHint },
                true,
                true,
            };
        }

        [Theory]
        [MemberData(nameof(AnalyzerSets))]
        public void IndependentAnalyzerSetsEmitOnlyOwnedUniqueHints(
            string name,
            ISourceGenerator[] generators,
            string[] expectedHints,
            bool includeR2fu,
            bool includeBridge)
        {
            var execution = Run(
                generators,
                includeR2fu,
                includeBridge);
            var result = execution.RunResult;
            var generated = Sources(result);
            var ownedHints = generated.Keys
                .Where(hint =>
                    string.Equals(hint, CoreHint, StringComparison.Ordinal)
                    || string.Equals(hint, R2fuHint, StringComparison.Ordinal)
                    || string.Equals(hint, BridgeHint, StringComparison.Ordinal))
                .OrderBy(hint => hint, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                expectedHints.OrderBy(
                    hint => hint,
                    StringComparer.Ordinal),
                ownedHints);
            Assert.Equal(
                generated.Count,
                generated.Keys
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.All(
                result.Results,
                generatorResult =>
                    Assert.Null(generatorResult.Exception));
            Assert.Empty(
                result.Diagnostics.Where(diagnostic =>
                    diagnostic.Severity
                    == DiagnosticSeverity.Error));
            Assert.DoesNotContain(
                execution.GeneratorDiagnostics,
                diagnostic =>
                    diagnostic.Severity
                    == DiagnosticSeverity.Error);
            Assert.DoesNotContain(
                execution.OutputCompilation.GetDiagnostics(),
                diagnostic =>
                    diagnostic.Severity
                    == DiagnosticSeverity.Error);
            Assert.False(
                string.IsNullOrWhiteSpace(name));
        }

        [Fact]
        public void PhysicalAndRoslynProviderEmittersStayEquivalent()
        {
            var generated = Sources(
                Run(
                    FoxRunAnalyzerTestComposition
                        .AllProviders(),
                    includeR2fu: true,
                    includeBridge: true)
                    .RunResult);
            var type = PhysicalType();

            Assert.Equal(
                FoxRunR2fuSourceEmitter.Emit(type),
                generated[R2fuHint]);
            Assert.Equal(
                FoxRunBridgeSourceEmitter
                    .EmitBridgeContribution(type),
                generated[BridgeHint]);
            Assert.Contains(
                "IFoxRunBridgeGeneratedSubscribeSource",
                generated[BridgeHint],
                StringComparison.Ordinal);
            Assert.Contains(
                "unity2foxglove_foxrun_interfaces_v1/msg/"
                + "Phase181State48D288ED82F1Envelope",
                generated[BridgeHint],
                StringComparison.Ordinal);
            Assert.Contains(
                "04c0e0d39b4c108bdb86e242f44215e394f5f56175e18a8ab60c682987e8b422",
                generated[BridgeHint],
                StringComparison.Ordinal);
            Assert.Contains(
                "writer.WriteUInt16((ushort)",
                generated[BridgeHint],
                StringComparison.Ordinal);
            Assert.Contains(
                "reader.ReadUInt16()",
                generated[BridgeHint],
                StringComparison.Ordinal);
        }

        private static AnalyzerExecution Run(
            ISourceGenerator[] generators,
            bool includeR2fu,
            bool includeBridge)
        {
            var preprocessorSymbols =
                includeR2fu
                    ? new[]
                    {
                        "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                        "UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES",
                        "COMPOSITION_R2FU",
                    }
                    : Array.Empty<string>();
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: preprocessorSymbols);
            var compilation = CSharpCompilation.Create(
                "phase186_analyzer_composition_contract",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        FixtureSource,
                        parseOptions),
                },
                PlatformReferences(
                    includeR2fu,
                    includeBridge),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver =
                CSharpGeneratorDriver.Create(
                    generators,
                    parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out var generatorDiagnostics);
            return new AnalyzerExecution(
                driver.GetRunResult(),
                outputCompilation,
                generatorDiagnostics);
        }

        private static ISourceGenerator[] WithCore(
            ISourceGenerator[] providerGenerators)
            => FoxRunAnalyzerTestComposition.CoreOnly()
                .Concat(providerGenerators)
                .ToArray();

        private static Dictionary<string, string> Sources(
            GeneratorDriverRunResult result)
            => result.Results
                .SelectMany(item => item.GeneratedSources)
                .ToDictionary(
                    item => item.HintName,
                    item => item.SourceText.ToString(),
                    StringComparer.Ordinal);

        private static MetadataReference[] PlatformReferences(
            bool includeR2fu,
            bool includeBridge)
        {
            var trusted = (string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES");
            var references = trusted
                .Split(Path.PathSeparator)
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path))
                .Concat(
                    new[]
                    {
                        typeof(FoxRunAttribute).Assembly.Location,
                    });
            if (includeR2fu)
            {
                references = references.Concat(
                    new[]
                    {
                        typeof(
                            Unity2Foxglove.Ros2ForUnity.Native
                                .FoxRunRos2TransportProvider)
                            .Assembly.Location,
                        typeof(
                            unity2foxglove_foxrun_interfaces_v1.msg
                                .Phase181State48D288ED82F1Envelope)
                            .Assembly.Location,
                    });
            }
            if (includeBridge)
            {
                references = references.Concat(
                    new[]
                    {
                        typeof(
                            Unity2Foxglove.Ros2Bridge
                                .Ros2BridgeFrame)
                            .Assembly.Location,
                    });
            }

            return references
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    path => MetadataReference
                        .CreateFromFile(path))
                .ToArray();
        }

        private sealed class AnalyzerExecution
        {
            internal AnalyzerExecution(
                GeneratorDriverRunResult runResult,
                Compilation outputCompilation,
                IEnumerable<Diagnostic> generatorDiagnostics)
            {
                RunResult = runResult;
                OutputCompilation = outputCompilation;
                GeneratorDiagnostics =
                    generatorDiagnostics.ToArray();
            }

            internal GeneratorDriverRunResult RunResult { get; }
            internal Compilation OutputCompilation { get; }
            internal Diagnostic[] GeneratorDiagnostics { get; }
        }

        private static FoxRunGenerationType PhysicalType()
        {
            var r2fuType =
                typeof(
                    Unity.FoxgloveSDK.Tests.FoxRun.Fixtures
                        .Phase181State);
            var bridgeType = r2fuType;
            var members = new[]
            {
                new FoxRunGenerationMember(
                    Namespace,
                    ClassName,
                    "_core",
                    "field",
                    typeof(int).FullName,
                    isValueType: true,
                    isArray: false,
                    elementTypeName: string.Empty,
                    topic:
                        "/phase186/composition/core",
                    hz: -1f,
                    schemaName: string.Empty,
                    policy: 1,
                    tolerance: 0f,
                    hostKind: "field",
                    rawMemberOrder: 0,
                    conditionalSymbols: string.Empty,
                    typeShape:
                        FoxRunReflectionTypeShapeBuilder
                            .Build(typeof(int))),
                new FoxRunGenerationMember(
                    Namespace,
                    ClassName,
                    "_r2fu",
                    "field",
                    r2fuType.FullName,
                    isValueType: false,
                    isArray: false,
                    elementTypeName: string.Empty,
                    topic:
                        "/phase186/composition/r2fu",
                    hz: -1f,
                    schemaName: string.Empty,
                    policy: 1,
                    tolerance: 0f,
                    hostKind: "field",
                    rawMemberOrder: 1,
                    conditionalSymbols: string.Empty,
                    mode: 3,
                    typeShape:
                        FoxRunReflectionTypeShapeBuilder
                            .Build(r2fuType),
                    generatesWebSocketCodec: false,
                    namedArgumentPresence:
                        FoxRunNamedArgumentPresence.Mode
                        | FoxRunNamedArgumentPresence
                            .PublishTransportIds
                        | FoxRunNamedArgumentPresence
                            .SubscribeTransportId,
                    publishTransportIds: new[]
                    {
                        "unity2foxglove.r2fu",
                    },
                    subscribeTransportId:
                        "unity2foxglove.r2fu"),
                new FoxRunGenerationMember(
                    Namespace,
                    ClassName,
                    "_bridge",
                    "field",
                    bridgeType.FullName,
                    isValueType: false,
                    isArray: false,
                    elementTypeName: string.Empty,
                    topic:
                        "/phase186/composition/bridge",
                    hz: -1f,
                    schemaName: string.Empty,
                    policy: 1,
                    tolerance: 0f,
                    hostKind: "field",
                    rawMemberOrder: 2,
                    conditionalSymbols: string.Empty,
                    mode: 3,
                    typeShape:
                        FoxRunReflectionTypeShapeBuilder
                            .Build(bridgeType),
                    generatesWebSocketCodec: false,
                    namedArgumentPresence:
                        FoxRunNamedArgumentPresence.Mode
                        | FoxRunNamedArgumentPresence
                            .PublishTransportIds
                        | FoxRunNamedArgumentPresence
                            .SubscribeTransportId,
                    publishTransportIds: new[]
                    {
                        "unity2foxglove.ros2bridge",
                    },
                    subscribeTransportId:
                        "unity2foxglove.ros2bridge"),
            };
            return Assert.Single(
                FoxRunGenerationModel
                    .FromMembers(members)
                    .Types);
        }
    }
}
