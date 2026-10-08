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
        [Fact]
        [Trait("Phase", "185-C")]
        public void MessagePackTriggerConditionDiscardsBlockedTransaction()
        {
            var compilation = CSharpCompilation.Create(
                "Phase185MessagePackCondition_" + Guid.NewGuid().ToString("N"),
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
    public partial class ConditionalReceiver
    {
        public bool Enabled;

        [FoxRun(""/phase185/conditional"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack,
            Policy = FoxRunPolicy.Trigger,
            OnlyIf = ""Enabled"")]
        public int Value;
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
                "Generated conditional consumer failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType(
                "Demo.ConditionalReceiver",
                throwOnError: true);
            var receiver = Activator.CreateInstance(receiverType);
            var transactional =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    receiver);
            var apply = receiverType.GetMethod("FoxRun_Apply_Value");

            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    SingleIntPayload("Value", 5),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var firstError),
                firstError);
            Assert.False(Assert.IsType<bool>(apply!.Invoke(receiver, null)));
            Assert.Equal(0, receiverType.GetField("Value")!.GetValue(receiver));

            receiverType.GetField("Enabled")!.SetValue(receiver, true);
            Assert.False(Assert.IsType<bool>(apply.Invoke(receiver, null)));
            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    SingleIntPayload("Value", 5),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var secondError),
                secondError);
            Assert.True(Assert.IsType<bool>(apply.Invoke(receiver, null)));
            Assert.Equal(5, receiverType.GetField("Value")!.GetValue(receiver));
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void GeneratedStreamConsumerReservesCancelsAndCommitsAcrossAssemblyBoundary()
        {
            var compilation = CSharpCompilation.Create(
                "Phase185MessagePackStream_" + Guid.NewGuid().ToString("N"),
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
    public partial class StreamReceiver
    {
        [FoxRun(""/phase185/stream"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public FoxRunStream<int> Samples = new FoxRunStream<int>(
            new FoxRunStreamOptions(
                capacity: 2,
                maxInputHz: 1000000000d,
                maxBatch: 2,
                overflow: FoxRunStreamOverflowPolicy.DropOldest));
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
                "Generated stream consumer failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType(
                "Demo.StreamReceiver",
                throwOnError: true);
            var receiver = Activator.CreateInstance(receiverType);
            var transactional =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    receiver);
            var owned =
                Assert.IsAssignableFrom<IFoxgloveTransactionalOwnedInputSource>(
                    receiver);
            Assert.True(
                owned.FoxgloveInput_TryAcquireTransactionalOwned(
                    0,
                    out var ownershipError),
                ownershipError);

            using (var malformed = new FoxgloveMsgPackWriter())
            {
                malformed.WriteMapHeader(1);
                malformed.WriteString("Samples");
                malformed.WriteString("wrong-type");
                Assert.False(
                    transactional.FoxgloveInput_TryStageTransaction(
                        0,
                        malformed.ToArray(),
                        FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                        out var malformedError));
                Assert.False(string.IsNullOrWhiteSpace(malformedError));
            }

            using (var valid = new FoxgloveMsgPackWriter())
            {
                valid.WriteMapHeader(1);
                valid.WriteString("Samples");
                valid.WriteInt32(9);
                Assert.True(
                    transactional.FoxgloveInput_TryStageTransaction(
                        0,
                        valid.ToArray(),
                        FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                        out var validError),
                    validError);
            }

            var stream = Assert.IsType<FoxRunStream<int>>(
                receiverType.GetField("Samples")!.GetValue(receiver));
            Assert.True(stream.TryTake(out var sample));
            Assert.Equal(9, sample.Value);
            sample.Dispose();

            owned.FoxgloveInput_ClearTransactionalOwned(0);
            Assert.Equal(0, stream.Count);
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void GeneratedConsumerMaterializesNestedTypedShape()
        {
            var compilation = CSharpCompilation.Create(
                "Phase185MessagePackNested_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public enum Mode { Idle = -1, Active = 2 }
    public sealed class Nested { public int Value; }
    public sealed class Envelope
    {
        public Mode Mode;
        public Nested Nested;
        public int? Optional;
        public byte[] Payload;
        public Vector3 Position;
        public List<int> Samples;
    }

    public partial class NestedReceiver
    {
        [FoxRun(""/phase185/nested"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public Envelope Value;
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
                "Generated nested consumer failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType(
                "Demo.NestedReceiver",
                throwOnError: true);
            var receiver = Activator.CreateInstance(receiverType);
            var transactional =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    receiver);
            var input = Assert.IsAssignableFrom<IFoxgloveInputSource>(receiver);

            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    NestedEnvelopePayload(
                        duplicateUnknownNestedKey: false),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(4096),
                    out var error),
                error);
            Assert.False(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    NestedEnvelopePayload(
                        duplicateUnknownNestedKey: true),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(4096),
                    out var duplicateError));
            Assert.Contains(
                "duplicate",
                duplicateError,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, input.FoxgloveInput_Flush(1d, 60));

            var envelope = receiverType.GetField("Value")!.GetValue(receiver);
            var envelopeType = assembly.GetType("Demo.Envelope", true);
            Assert.Equal(
                2,
                Convert.ToInt32(envelopeType.GetField("Mode")!.GetValue(envelope)));
            var nested = envelopeType.GetField("Nested")!.GetValue(envelope);
            Assert.Equal(
                7,
                assembly.GetType("Demo.Nested", true)
                    .GetField("Value")!
                    .GetValue(nested));
            Assert.Null(envelopeType.GetField("Optional")!.GetValue(envelope));
            Assert.Equal(
                new byte[] { 0xaa, 0xbb },
                envelopeType.GetField("Payload")!.GetValue(envelope));
            var position = (UnityEngine.Vector3)envelopeType
                .GetField("Position")!
                .GetValue(envelope);
            Assert.Equal(1f, position.x);
            Assert.Equal(2f, position.y);
            Assert.Equal(3f, position.z);
            var samples = (System.Collections.IList)envelopeType
                .GetField("Samples")!
                .GetValue(envelope);
            Assert.Equal(new[] { 3, 4 }, samples.Cast<int>().ToArray());
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void GeneratedDecoderAcceptsWireDepthThirtyThreeAndThirtyFourButRejectsThirtyFive()
        {
            var source = new StringBuilder(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
");
            AppendNestedTypes(source, "Depth33", chainLinks: 30);
            AppendNestedTypes(source, "Depth34", chainLinks: 31);
            source.Append(@"
    public partial class Depth33Receiver
    {
        [FoxRun(""/phase185/depth33"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public Depth33Node0 Value;
    }

    public partial class Depth34Receiver
    {
        [FoxRun(""/phase185/depth34"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public Depth34Node0 Value;
    }
}
");

            var compilation = CSharpCompilation.Create(
                "Phase185MessagePackDepth_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source.ToString()) },
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
                "Generated depth fixture failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            AssertGeneratedDepthAccepted(
                assembly,
                "Demo.Depth33Receiver",
                NestedObjectPayload(chainLinks: 30));
            AssertGeneratedDepthAccepted(
                assembly,
                "Demo.Depth34Receiver",
                NestedObjectPayload(chainLinks: 31));

            var receiver = Activator.CreateInstance(
                assembly.GetType("Demo.Depth34Receiver", throwOnError: true));
            var transactional =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    receiver);
            Assert.False(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    DepthThirtyFiveUnknownFieldPayload(),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(4096),
                    out var error));
            Assert.Contains("depth", error, StringComparison.OrdinalIgnoreCase);
        }

    }
}
