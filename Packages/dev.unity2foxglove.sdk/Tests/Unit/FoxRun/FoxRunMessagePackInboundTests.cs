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
    public sealed class FoxRunMessagePackInboundTests
    {
        [Fact]
        [Trait("Phase", "185-C")]
        public void GeneratedConsumerDecodesOneAtomicTopicTransaction()
        {
            var compilation = CSharpCompilation.Create(
                "Phase185MessagePackInbound_" + Guid.NewGuid().ToString("N"),
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
    public partial class MessagePackReceiver
    {
        [FoxRun(""/phase185/atomic"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack, Hz = 60f)]
        public int Count;

        [FoxRun(""/phase185/atomic"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack, Hz = 60f)]
        public string Label;
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
                "Generated transactional consumer failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType(
                "Demo.MessagePackReceiver",
                throwOnError: true);
            var receiver = Activator.CreateInstance(receiverType);
            var input = Assert.IsAssignableFrom<IFoxgloveInputSource>(receiver);
            var transactional =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    receiver);
            Assert.Equal(1, transactional.FoxgloveInput_TransactionCount);
            Assert.Equal(
                FoxRunEncoding.MessagePack,
                transactional.FoxgloveInput_GetTransaction(0).DeclaredEncoding);

            var first = Payload(1, "first", reverseOrder: true);
            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    first,
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var firstError),
                firstError);

            var malformed = PayloadWithDuplicateCount();
            Assert.False(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    malformed,
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var malformedError));
            Assert.Contains(
                "duplicate",
                malformedError,
                StringComparison.OrdinalIgnoreCase);

            Assert.Equal(1, input.FoxgloveInput_Flush(1d, 60));
            Assert.Equal(1, receiverType.GetField("Count")!.GetValue(receiver));
            Assert.Equal(
                "first",
                receiverType.GetField("Label")!.GetValue(receiver));
            Assert.Equal(0, input.FoxgloveInput_Flush(2d, 60));

            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    Payload(2, "second", reverseOrder: false),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var secondError),
                secondError);
            transactional.FoxgloveInput_ClearTransaction(0);
            Assert.Equal(0, input.FoxgloveInput_Flush(3d, 60));
            Assert.Equal(1, receiverType.GetField("Count")!.GetValue(receiver));
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
                source: FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource,
                namedArgumentPresence:
                    FoxRunNamedArgumentPresence.Source
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
                source:
                    FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource);

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

            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(1);
            writer.WriteString("Value");
            writer.WriteMapHeader(6);
            writer.WriteString("Samples");
            writer.WriteArrayHeader(2);
            writer.WriteInt32(3);
            writer.WriteInt32(4);
            writer.WriteString("Position");
            writer.WriteMapHeader(3);
            writer.WriteString("z");
            writer.WriteFloat(3f);
            writer.WriteString("x");
            writer.WriteFloat(1f);
            writer.WriteString("y");
            writer.WriteFloat(2f);
            writer.WriteString("Payload");
            writer.WriteBinary(new byte[] { 0xaa, 0xbb });
            writer.WriteString("Optional");
            writer.WriteNil();
            writer.WriteString("Nested");
            writer.WriteMapHeader(1);
            writer.WriteString("Value");
            writer.WriteInt32(7);
            writer.WriteString("Mode");
            writer.WriteInt32(2);

            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    writer.ToArray(),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(4096),
                    out var error),
                error);
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

        private static byte[] Payload(
            int count,
            string label,
            bool reverseOrder)
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(2);
            if (reverseOrder)
            {
                writer.WriteString("Label");
                writer.WriteString(label);
                writer.WriteString("Count");
                writer.WriteInt32(count);
            }
            else
            {
                writer.WriteString("Count");
                writer.WriteInt32(count);
                writer.WriteString("Label");
                writer.WriteString(label);
            }
            return writer.ToArray();
        }

        private static byte[] PayloadWithDuplicateCount()
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(3);
            writer.WriteString("Count");
            writer.WriteInt32(1);
            writer.WriteString("Label");
            writer.WriteString("bad");
            writer.WriteString("Count");
            writer.WriteInt32(2);
            return writer.ToArray();
        }

        private static byte[] SingleIntPayload(string field, int value)
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(1);
            writer.WriteString(field);
            writer.WriteInt32(value);
            return writer.ToArray();
        }

        private static void AppendNestedTypes(
            StringBuilder source,
            string prefix,
            int chainLinks)
        {
            for (var depth = 0; depth < chainLinks; depth++)
            {
                source.Append("    public sealed class ")
                    .Append(prefix).Append("Node").Append(depth)
                    .Append(" { public ").Append(prefix).Append("Node")
                    .Append(depth + 1).AppendLine(" Next; }");
            }
            source.Append("    public sealed class ")
                .Append(prefix).Append("Node").Append(chainLinks)
                .AppendLine(" { public UnityEngine.Vector3 Leaf; }");
        }

        private static byte[] NestedObjectPayload(int chainLinks)
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(1);
            writer.WriteString("Value");
            for (var depth = 0; depth < chainLinks; depth++)
            {
                writer.WriteMapHeader(1);
                writer.WriteString("Next");
            }
            writer.WriteMapHeader(1);
            writer.WriteString("Leaf");
            WriteVector3(writer);
            return writer.ToArray();
        }

        private static byte[] DepthThirtyFiveUnknownFieldPayload()
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(2);
            writer.WriteString("Unknown");
            for (var depth = 0; depth < 34; depth++)
                writer.WriteArrayHeader(1);
            writer.WriteNil();
            writer.WriteString("Value");
            for (var depth = 0; depth < 31; depth++)
            {
                writer.WriteMapHeader(1);
                writer.WriteString("Next");
            }
            writer.WriteMapHeader(1);
            writer.WriteString("Leaf");
            WriteVector3(writer);
            return writer.ToArray();
        }

        private static void WriteVector3(FoxgloveMsgPackWriter writer)
        {
            writer.WriteMapHeader(3);
            writer.WriteString("x");
            writer.WriteFloat(1f);
            writer.WriteString("y");
            writer.WriteFloat(2f);
            writer.WriteString("z");
            writer.WriteFloat(3f);
        }

        private static void AssertGeneratedDepthAccepted(
            Assembly assembly,
            string receiverTypeName,
            byte[] payload)
        {
            var receiver = Activator.CreateInstance(
                assembly.GetType(receiverTypeName, throwOnError: true));
            var transactional =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    receiver);
            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    payload,
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(4096),
                    out var error),
                error);
        }

        private static MetadataReference[] DynamicCompilationReferences()
        {
            var locations = ((string)AppContext.GetData(
                                 "TRUSTED_PLATFORM_ASSEMBLIES")
                             ?? string.Empty)
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Append(typeof(FoxRunEncoding).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return locations
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }
    }
}
