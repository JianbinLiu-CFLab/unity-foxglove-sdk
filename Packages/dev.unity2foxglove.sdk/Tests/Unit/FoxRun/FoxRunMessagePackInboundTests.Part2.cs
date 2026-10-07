// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.SourceGenerators;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunMessagePackInboundTests
    {
        [Theory]
        [InlineData("char")]
        [InlineData("decimal")]
        [InlineData("object")]
        [InlineData("System.DateTime")]
        [InlineData("System.DateTimeOffset")]
        [InlineData("System.Guid")]
        [InlineData("System.TimeSpan")]
        [Trait("Phase", "185-F")]
        public void ExplicitUnsupportedScalarShapesFailClosedForBothDirections(
            string typeName)
        {
            foreach (var flow in new[] { "Publish", "Subscribe" })
            {
                var driver = RunGenerator(
                    PrimitiveReceiverSource(
                        typeName,
                        "ExplicitScalar" + flow,
                        ", Encoding = FoxRunEncoding.MessagePack",
                        flow),
                    out var output,
                    out var generatorDiagnostics);
                var allDiagnostics = generatorDiagnostics
                    .Concat(driver.GetRunResult().Diagnostics)
                    .Concat(output.GetDiagnostics())
                    .ToArray();
                var generated = GeneratedText(driver);

                Assert.Contains(
                    allDiagnostics,
                    diagnostic => diagnostic.Id == "FOXRUN616");
                Assert.DoesNotContain(
                    allDiagnostics,
                    diagnostic => diagnostic.Id == "CS8785");
                Assert.DoesNotContain(
                    "__TryReadFoxRunMessagePackValue_",
                    generated,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "__BuildFoxRunMessagePack_",
                    generated,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "IFoxgloveTransactionalInputSource",
                    generated,
                    StringComparison.Ordinal);
            }
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ExplicitUnsupportedCollectionReportsFoxRun616WithoutFallback()
        {
            var driver = RunGenerator(@"
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class UnsupportedCollectionReceiver
    {
        [FoxRun(""/phase185f/unsupported-collection"",
            Mode = FoxRunFlow.Subscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public HashSet<int> Values;
    }
}",
                out var output,
                out var generatorDiagnostics);
            var allDiagnostics = generatorDiagnostics
                .Concat(driver.GetRunResult().Diagnostics)
                .Concat(output.GetDiagnostics())
                .ToArray();
            var generated = GeneratedText(driver);

            Assert.Contains(
                allDiagnostics,
                diagnostic => diagnostic.Id == "FOXRUN616");
            Assert.DoesNotContain(
                allDiagnostics,
                diagnostic => diagnostic.Id == "CS8785");
            Assert.DoesNotContain(
                "__TryReadFoxRunMessagePackValue_",
                generated,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "IFoxgloveTransactionalInputSource",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void DuplicateNestedJsonNamesEmitNoMessagePackCodec()
        {
            var driver = RunGenerator(@"
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public sealed class DuplicatePayload
    {
        [JsonProperty(""same"")] public int First;
        [JsonProperty(""same"")] public int Second;
    }

    public partial class DuplicatePayloadDuplex
    {
        [FoxRun(""/phase185f/duplicate-json"",
            Mode = FoxRunFlow.PublishAndSubscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public DuplicatePayload Value;
    }
}",
                out var output,
                out var generatorDiagnostics);
            var allDiagnostics = generatorDiagnostics
                .Concat(driver.GetRunResult().Diagnostics)
                .Concat(output.GetDiagnostics())
                .ToArray();
            var generated = GeneratedText(driver);

            Assert.Contains(
                allDiagnostics,
                diagnostic => diagnostic.Id == "FOXRUN616");
            Assert.DoesNotContain(
                allDiagnostics,
                diagnostic => diagnostic.Id == "CS8785");
            Assert.DoesNotContain(
                "__BuildFoxRunMessagePack_",
                generated,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "__TryReadFoxRunMessagePackValue_",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void NonGenericValueTupleEmitsNoMessagePackCodec()
        {
            var driver = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class TupleDuplex
    {
        [FoxRun(""/phase185f/value-tuple"",
            Mode = FoxRunFlow.PublishAndSubscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public System.ValueTuple Value;
    }
}",
                out var output,
                out var generatorDiagnostics);
            var allDiagnostics = generatorDiagnostics
                .Concat(driver.GetRunResult().Diagnostics)
                .Concat(output.GetDiagnostics())
                .ToArray();
            var generated = GeneratedText(driver);

            Assert.Contains(
                allDiagnostics,
                diagnostic => diagnostic.Id == "FOXRUN616");
            Assert.DoesNotContain(
                allDiagnostics,
                diagnostic => diagnostic.Id == "CS8785");
            Assert.DoesNotContain(
                "__BuildFoxRunMessagePack_",
                generated,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "__TryReadFoxRunMessagePackValue_",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void GeneratedSourceDeclaresDistinctTransactionalSurface()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "Value",
                "System.Int32",
                "/phase185/source",
                10f,
                string.Empty,
                policy: 1,
                tolerance: 0f,
                mode: 2,
                canonicalType: "int32",
                encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape: FoxRunTypeShape.Canonical("int32"),
                subscribeTransportId: FoxgloveWebSocketTransport.Id,
                namedArgumentPresence:
                    FoxRunNamedArgumentPresence.SubscribeTransportId
                    | FoxRunNamedArgumentPresence.Encoding);

            var generated = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "MessagePackSource",
                new[] { member });

            Assert.Contains(
                "IFoxgloveTransactionalInputSource",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxgloveInput_TransactionCount",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxgloveInput_TryStageTransaction",
                generated,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "FoxgloveInput_TryStage(transactionIndex",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void InheritedLegacyCanonicalShapeKeepsExistingInputWithoutTransaction()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "Position",
                "UnityEngine.Vector3",
                "/phase185/legacy-vector",
                10f,
                string.Empty,
                policy: 1,
                tolerance: 0f,
                mode: 2,
                canonicalType: "unity.vector3.float32",
                encoding:
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                typeShape:
                    FoxRunTypeShape.Canonical("unity.vector3.float32"),
                subscribeTransportId:
                    FoxgloveWebSocketTransport.Id);

            var generated = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "LegacyVectorReceiver",
                new[] { member });

            Assert.Contains(
                "IFoxgloveInputSource",
                generated,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "IFoxgloveTransactionalInputSource",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxgloveInput_TryStage",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void MixedLegacyAndMessagePackTriggerInputsCompileWithDistinctIndexes()
        {
            var compilation = CSharpCompilation.Create(
                "Phase185MessagePackTrigger_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class TriggerReceiver
    {
        [FoxRun(""/phase185/a-messagepack"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack,
            Policy = FoxRunPolicy.Trigger)]
        public int MessagePackValue;

        [FoxRun(""/phase185/b-json"", Mode = Subscribe,
            Encoding = FoxRunEncoding.JSON,
            Policy = FoxRunPolicy.Trigger)]
        public int JsonValue;
    }
}")
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver =
                CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var output,
                out _);
            using var image = new MemoryStream();
            var emit = output.Emit(image);

            Assert.True(
                emit.Success,
                "Generated mixed trigger consumer failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        }

    }
}
