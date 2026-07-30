// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: RED contract for deterministic generated typed MessagePack publication.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    [Trait("Phase", "185-B")]
    [Trait("Domain", "FoxRun")]
    public sealed class FoxRunMessagePackEmitterTests
    {
        [Fact]
        public void GeneratedMessagePackUsesDirectTypedWriterCallsInStableNestedMapOrder()
        {
            var nested = FoxRunTypeShape.Object(
                "Demo.Pose",
                new[]
                {
                    new FoxRunTypeField("zeta", "Zeta", FoxRunTypeShape.Canonical("float64")),
                    new FoxRunTypeField("alpha", "Alpha", FoxRunTypeShape.Canonical("int32"))
                });
            var root = FoxRunTypeShape.Object(
                "Demo.Telemetry",
                new[]
                {
                    new FoxRunTypeField("pose", "Pose", nested),
                    new FoxRunTypeField("enabled", "Enabled", FoxRunTypeShape.Canonical("bool"))
                });

            var source = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "TelemetrySource",
                new[] { Member("_telemetry", "Demo.Telemetry", root) });

            Assert.Contains(
                "new global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackWriter",
                source,
                StringComparison.Ordinal);
            Assert.Contains("WriteMapHeader(2)", source, StringComparison.Ordinal);
            AssertInOrder(
                source,
                "WriteString(\"telemetry\")",
                "WriteString(\"enabled\")",
                "WriteString(\"pose\")",
                "WriteString(\"alpha\")",
                "WriteString(\"zeta\")");
            Assert.Contains("WriteBool(", source, StringComparison.Ordinal);
            Assert.Contains("WriteInt32(", source, StringComparison.Ordinal);
            Assert.Contains("WriteDouble(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Dictionary<string, object>", source, StringComparison.Ordinal);
            Assert.DoesNotContain("JsonConvert", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        }

        [Fact]
        public void OneImmutableMessagePackPayloadIsBuiltOnceAndReusedDuringCapture()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "Counter",
                new[] { Member("_count", "System.Int32", FoxRunTypeShape.Canonical("int32")) });

            Assert.Single(
                Regex.Matches(
                        source,
                        "private byte\\[\\] __foxRunLastMessagePack_0;")
                    .Cast<Match>());
            var beginCapture = Slice(
                source,
                "bool IFoxglovePublishCaptureSource.FoxgloveLog_BeginCapture",
                "void IFoxglovePublishCaptureSource.FoxgloveLog_EndCapture");
            Assert.Single(
                Regex.Matches(
                        beginCapture,
                        "__BuildFoxRunMessagePack_0\\(\\)")
                    .Cast<Match>());
            Assert.Contains("__foxRunLastMessagePack_0 = __payload_0;", source, StringComparison.Ordinal);
            Assert.Contains("__foxRunLastMessagePack_0", source, StringComparison.Ordinal);
            Assert.Contains("__foxRunLastMessagePack_0 = null;", source, StringComparison.Ordinal);
        }

        [Fact]
        public void MessagePackWebSocketAndSinksConsumeOneNeutralCaptureCache()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_count",
                "System.Int32",
                "/phase185/duplex",
                10f,
                "Demo.Count",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape: FoxRunTypeShape.Canonical("int32"),
                publishTransportIds: new[]
                {
                    FoxgloveWebSocketTransport.Id,
                    "unity2foxglove.r2fu",
                    "unity2foxglove.ros2bridge"
                });

            var source = FoxgloveSourceEmitter.EmitClass("Demo", "Counter", new[] { member });

            Assert.Contains("PublishFoxRunMessagePackBytes(", source, StringComparison.Ordinal);
            Assert.Contains(
                "router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0), FoxRunEncoding.MessagePack, nowNs, __foxRunLastMessagePack_0",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRun_PublishRos2", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PublishRos2BridgeCdr", source, StringComparison.Ordinal);
            Assert.Contains("msgpack", source, StringComparison.Ordinal);
        }

        [Fact]
        public void InheritedMessagePackFreezesWebSocketEncodingBeforeCaptureAndSinkFanout()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_count",
                "System.Int32",
                "/phase185/inherited-recording",
                10f,
                "Demo.Count",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding,
                typeShape: FoxRunTypeShape.Canonical("int32"));

            var source = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "InheritedCounter",
                new[] { member });
            var beginCapture = Slice(
                source,
                "bool IFoxglovePublishCaptureSource.FoxgloveLog_BeginCapture",
                "void IFoxglovePublishCaptureSource.FoxgloveLog_EndCapture");
            var encodingSetter = Slice(
                source,
                "void IFoxRunWebSocketCaptureSource.FoxgloveLog_SetWebSocketEncoding",
                "[Preserve]");
            Assert.Contains(
                "__foxRunCaptureEncoding_0 = encoding;",
                encodingSetter,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (__foxRunCaptureEncoding_0 == FoxRunEncoding.MessagePack)",
                beginCapture,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (__foxRunCaptureEncoding_0 == FoxRunEncoding.MessagePack)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "router.PublishCompatible",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "ResolveFoxRunEncoding((FoxRunEncoding)0, FoxRunFlow.Publish)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0), FoxRunEncoding.MessagePack, nowNs, __foxRunLastMessagePack_0",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "else if (__foxRunCaptureEncoding_0 == FoxRunEncoding.JSON)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "Frozen FoxRun publish encoding is unsupported.",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void NullableUnityValuePublisherCompilesAndEmitsNil()
        {
            var assembly = CompileGenerated(@"
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class NullableUnityPublisher
    {
        [FoxRun(""/phase185f/nullable-vector"", Mode = FoxRunFlow.Publish,
            Encoding = FoxRunEncoding.MessagePack)]
        public Vector3? Position;
    }
}");
            var type = assembly.GetType(
                "Demo.NullableUnityPublisher",
                throwOnError: true);
            var instance = Activator.CreateInstance(type);
            var topicIndex = FindTopicIndex(
                instance,
                "/phase185f/nullable-vector");

            Assert.True(BeginCapture(instance, topicIndex));
            var payload = Assert.IsType<byte[]>(
                type.GetField(
                        "__foxRunLastMessagePack_" + topicIndex,
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(instance));
            var reader = new FoxgloveMsgPackReader(
                payload,
                FoxgloveMsgPackReadLimits.ForPayloadBytes(1024));
            Assert.True(reader.TryReadMapHeader(out var count), reader.Error);
            Assert.Equal(1, count);
            Assert.True(reader.TryReadString(out var key), reader.Error);
            Assert.Equal("Position", key);
            Assert.True(reader.TryReadNil(out var isNil), reader.Error);
            Assert.True(isNil);
            Assert.True(reader.TryComplete(), reader.Error);
            EndCapture(instance, topicIndex);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void NullReferenceDtoAndCollectionElementsEmitNil()
        {
            var assembly = CompileGenerated(@"
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public sealed class Nested
    {
        public int Value;
    }

    public sealed class Envelope
    {
        public Nested Child;
        public List<Nested> Items;
    }

    public partial class NullableDtoPublisher
    {
        [FoxRun(""/phase185f/null-root"", Mode = FoxRunFlow.Publish,
            Encoding = FoxRunEncoding.MessagePack)]
        public Envelope Root;

        [FoxRun(""/phase185f/null-nested"", Mode = FoxRunFlow.Publish,
            Encoding = FoxRunEncoding.MessagePack)]
        public Envelope Nested = new Envelope
        {
            Child = null,
            Items = new List<Demo.Nested> { null },
        };
    }
}");
            var type = assembly.GetType(
                "Demo.NullableDtoPublisher",
                throwOnError: true);
            var instance = Activator.CreateInstance(type);

            var rootIndex = FindTopicIndex(instance, "/phase185f/null-root");
            Assert.True(BeginCapture(instance, rootIndex));
            var root = CapturedMessagePack(type, instance, rootIndex);
            Assert.True(root.TryReadMapHeader(out var rootCount), root.Error);
            Assert.Equal(1, rootCount);
            Assert.True(root.TryReadString(out _), root.Error);
            Assert.True(root.TryReadNil(out var rootNil), root.Error);
            Assert.True(rootNil);
            Assert.True(root.TryComplete(), root.Error);
            EndCapture(instance, rootIndex);

            var nestedIndex = FindTopicIndex(
                instance,
                "/phase185f/null-nested");
            Assert.True(BeginCapture(instance, nestedIndex));
            var nested = CapturedMessagePack(type, instance, nestedIndex);
            Assert.True(nested.TryReadMapHeader(out var topicCount), nested.Error);
            Assert.Equal(1, topicCount);
            Assert.True(nested.TryReadString(out _), nested.Error);
            Assert.True(nested.TryReadMapHeader(out var fieldCount), nested.Error);
            Assert.Equal(2, fieldCount);
            Assert.True(nested.TryReadString(out var childKey), nested.Error);
            Assert.Equal("Child", childKey);
            Assert.True(nested.TryReadNil(out var childNil), nested.Error);
            Assert.True(childNil);
            Assert.True(nested.TryReadString(out var itemsKey), nested.Error);
            Assert.Equal("Items", itemsKey);
            Assert.True(nested.TryReadArrayHeader(out var itemCount), nested.Error);
            Assert.Equal(1, itemCount);
            Assert.True(nested.TryReadNil(out var itemNil), nested.Error);
            Assert.True(itemNil);
            Assert.True(nested.TryComplete(), nested.Error);
            EndCapture(instance, nestedIndex);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void BundledCdrMessagePackPublisherRetainsNativeJsonBuilderAndCompiles()
        {
            var shape = FoxRunReflectionTypeShapeBuilder.Build(
                typeof(Foxglove.Pose));
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_pose",
                "Foxglove.Pose",
                "/phase185f/bundled-pose",
                10f,
                "foxglove_msgs/msg/Pose",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                encoding:
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape: shape,
                targets:
                    FoxRunGenerationDescriptorConstants.Ros2NativeTarget);
            var generated = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "BundledPosePublisher",
                new[] { member });
            var declaration = @"
namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class BundledPosePublisher
    {
        private Foxglove.Pose _pose = new Foxglove.Pose();
    }
}
";
            var compilation = CSharpCompilation.Create(
                "Phase185FBundledMessagePack_"
                + Guid.NewGuid().ToString("N"),
                GeneratedPublishSyntaxTrees(declaration, generated),
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);

            Assert.Contains(
                "private byte[] __BuildFoxRunJson_0()",
                generated,
                StringComparison.Ordinal);
            Assert.True(
                emit.Success,
                "Generated bundled MessagePack publisher failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(
                        diagnostic => diagnostic.ToString())));
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void InheritedUnavailableMessagePackShapeDoesNotCrashEmission()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_position",
                "UnityEngine.Vector3",
                "/phase185f/legacy-vector",
                10f,
                string.Empty,
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                canonicalType: "unity.vector3.float32",
                encoding:
                    FoxRunGenerationDescriptorConstants.InheritEncoding);

            var generated = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "LegacyVectorPublisher",
                new[] { member });
            var declaration = @"
namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class LegacyVectorPublisher
    {
        private UnityEngine.Vector3 _position;
    }
}
";
            var compilation = CSharpCompilation.Create(
                "Phase185FLegacyInherited_"
                + Guid.NewGuid().ToString("N"),
                GeneratedPublishSyntaxTrees(declaration, generated),
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);

            Assert.DoesNotContain(
                "__BuildFoxRunMessagePack_0",
                generated,
                StringComparison.Ordinal);
            Assert.True(
                emit.Success,
                "Inherited non-MessagePack declaration failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(
                        diagnostic => diagnostic.ToString())));
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void InheritedObserversBranchOnFrozenMessagePackEncoding()
        {
            const string topic = "/phase185f/inherited-observer";
            var assembly = CompileGenerated(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class InheritedObserverPublisher
    {
        [FoxRun(""" + topic + @""", Mode = FoxRunFlow.Publish)]
        public int Count = 7;
    }
}");
            var type = assembly.GetType(
                "Demo.InheritedObserverPublisher",
                throwOnError: true);
            var instance = Activator.CreateInstance(type);
            var topicIndex = FindTopicIndex(instance, topic);
            var logicalContract = GetContract(instance, topicIndex);

            Assert.Same(
                logicalContract,
                GetContract(instance, topicIndex));

            var messagePackBus = new FoxTopicBus();
            var messagePackEnvelopes =
                new List<FoxTopicEnvelope<byte[]>>();
            messagePackBus.Subscribe<byte[]>(
                topic,
                envelope => messagePackEnvelopes.Add(envelope));
            SetCaptureEncoding(
                type,
                instance,
                topicIndex,
                FoxRunEncoding.MessagePack);
            Assert.True(BeginCapture(instance, topicIndex));
            Assert.True(HasObservers(
                instance,
                topicIndex,
                messagePackBus));
            PublishCapturedToObservers(
                instance,
                topicIndex,
                messagePackBus,
                1851UL);
            PublishToBus(
                instance,
                topicIndex,
                messagePackBus,
                1852UL);

            Assert.Equal(2, messagePackEnvelopes.Count);
            var wireContract = logicalContract.ForWireEncoding(
                FoxRunEncoding.MessagePack);
            Assert.All(
                messagePackEnvelopes,
                envelope =>
                {
                    Assert.Same(wireContract, envelope.Contract);
                    Assert.Equal("msgpack", envelope.Contract.Encoding);
                    Assert.Equal(string.Empty, envelope.Contract.SchemaName);
                });
            Assert.Same(
                messagePackEnvelopes[0].Payload,
                messagePackEnvelopes[1].Payload);
            var reader = new FoxgloveMsgPackReader(
                messagePackEnvelopes[0].Payload,
                FoxgloveMsgPackReadLimits.ForPayloadBytes(1024));
            Assert.True(reader.TryReadMapHeader(out var mapCount), reader.Error);
            Assert.Equal(1, mapCount);
            Assert.True(reader.TryReadString(out var key), reader.Error);
            Assert.Equal("Count", key);
            Assert.True(reader.TryReadInt32(out var value), reader.Error);
            Assert.Equal(7, value);
            Assert.True(reader.TryComplete(), reader.Error);
            EndCapture(instance, topicIndex);

            var jsonBus = new FoxTopicBus();
            var jsonEnvelopes =
                new List<FoxTopicEnvelope<Dictionary<string, object>>>();
            jsonBus.Subscribe<Dictionary<string, object>>(
                topic,
                envelope => jsonEnvelopes.Add(envelope));
            SetCaptureEncoding(
                type,
                instance,
                topicIndex,
                FoxRunEncoding.JSON);
            Assert.True(BeginCapture(instance, topicIndex));
            Assert.True(HasObservers(instance, topicIndex, jsonBus));
            PublishCapturedToObservers(
                instance,
                topicIndex,
                jsonBus,
                1853UL);
            PublishToBus(
                instance,
                topicIndex,
                jsonBus,
                1854UL);

            Assert.Equal(2, jsonEnvelopes.Count);
            Assert.All(
                jsonEnvelopes,
                envelope =>
                {
                    Assert.Same(logicalContract, envelope.Contract);
                    Assert.Equal("json", envelope.Contract.Encoding);
                    Assert.Equal(7, Assert.IsType<int>(
                        envelope.Payload["Count"]));
                });
            EndCapture(instance, topicIndex);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ExplicitMessagePackObserversUseSchemaLessStableWireContract()
        {
            const string topic = "/phase185f/explicit-observer";
            var assembly = CompileGenerated(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class ExplicitObserverPublisher
    {
        [FoxRun(""" + topic + @""", Mode = FoxRunFlow.Publish,
            Encoding = FoxRunEncoding.MessagePack,
            SchemaName = ""Demo.Logical"")]
        public int Value = 9;
    }
}");
            var type = assembly.GetType(
                "Demo.ExplicitObserverPublisher",
                throwOnError: true);
            var instance = Activator.CreateInstance(type);
            var topicIndex = FindTopicIndex(instance, topic);
            var logicalContract = GetContract(instance, topicIndex);
            Assert.Equal("Demo.Logical", logicalContract.SchemaName);
            Assert.Same(
                logicalContract,
                GetContract(instance, topicIndex));

            var bus = new FoxTopicBus();
            var envelopes = new List<FoxTopicEnvelope<byte[]>>();
            bus.Subscribe<byte[]>(
                topic,
                envelope => envelopes.Add(envelope));
            Assert.True(BeginCapture(instance, topicIndex));
            PublishCapturedToObservers(
                instance,
                topicIndex,
                bus,
                1856UL);
            PublishToBus(
                instance,
                topicIndex,
                bus,
                1857UL);

            Assert.Equal(2, envelopes.Count);
            var wireContract = logicalContract.ForWireEncoding(
                FoxRunEncoding.MessagePack);
            Assert.All(
                envelopes,
                envelope =>
                {
                    Assert.Same(wireContract, envelope.Contract);
                    Assert.Equal("msgpack", envelope.Contract.Encoding);
                    Assert.Equal(string.Empty, envelope.Contract.SchemaName);
                });
            EndCapture(instance, topicIndex);
        }

        [Theory]
        [InlineData("JSON")]
        [InlineData("Protobuf")]
        [Trait("Phase", "185-F")]
        public void CompiledNonMessagePackSinkSideChannelExcludesTargetSinks(
            string encodingName)
        {
            const string topic = "/phase185f/compatible-sink";
            var assembly = CompileGenerated(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class CompatibleSinkPublisher
    {
        [FoxRun(""" + topic + @""", Mode = FoxRunFlow.Publish,
            Encoding = FoxRunEncoding." + encodingName + @")]
        public int Value = 42;
    }
}");
            var type = assembly.GetType(
                "Demo.CompatibleSinkPublisher",
                throwOnError: true);
            var instance = Activator.CreateInstance(type);
            var topicIndex = FindTopicIndex(instance, topic);
            var contract = GetContract(instance, topicIndex);
            var additive = new GeneratedRecordingSink();
            var target = new GeneratedTargetSink();
            var legacyTarget = new GeneratedLegacyTargetSink();
            var router = new FoxTopicSinkRouter();
            router.AddSink(additive);
            router.AddSink(target);
            router.AddSink(legacyTarget);
            Assert.True(router.Register(contract));

            var primary = router.PublishTarget(
                FoxRunEndpoint.Ros2Native,
                contract,
                1854UL,
                new byte[] { 0x01 },
                "primary");
            Assert.True(primary.Succeeded);
            Assert.Equal(0, additive.PublishCalls);
            Assert.Equal(1, target.PublishCalls);
            Assert.Equal(1, legacyTarget.PublishCalls);

            Assert.True(BeginCapture(instance, topicIndex));
            PublishToSinks(instance, topicIndex, router, 1855UL);

            Assert.Equal(1, additive.PublishCalls);
            Assert.Equal(1, target.PublishCalls);
            Assert.Equal(1, legacyTarget.PublishCalls);
            Assert.Equal("json", additive.LastContract.Encoding);
            Assert.Same(
                additive.RegisteredContract,
                additive.LastContract);
            Assert.Contains(
                "\"Value\":42",
                Encoding.UTF8.GetString(additive.LastPayload),
                StringComparison.Ordinal);
            EndCapture(instance, topicIndex);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void GeneratedNullableValuesAvoidBoxingAndStructObjectsShortCircuitNullChecks()
        {
            var nullable = Member(
                "_optional",
                "System.Nullable<System.Int32>",
                FoxRunTypeShape.Canonical("int32", nullable: true));
            var vector = new FoxgloveSourceEmitter.TopicMember(
                "_position",
                "UnityEngine.Vector3",
                "/phase185f/nonnullable-vector",
                10f,
                string.Empty,
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                encoding:
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape:
                    FoxRunReflectionTypeShapeBuilder.Build(
                        typeof(UnityEngine.Vector3)));
            var source = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "NoBoxingPublisher",
                new[] { nullable, vector });

            Assert.Contains(
                "if (!__foxRunCapture_0_0.HasValue)",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "if ((object)__foxRunCapture_0_0 == null)",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "typeof(global::UnityEngine.Vector3).IsValueType",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "if ((object)__value == null)",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void UnknownExplicitEncodingFailsClosedDuringEmission()
        {
            var member = Member(
                "_count",
                "System.Int32",
                FoxRunTypeShape.Canonical("int32"));
            member = new FoxgloveSourceEmitter.TopicMember(
                member.MemberName,
                member.TypeName,
                member.Topic,
                member.Hz,
                member.SchemaName,
                member.Policy,
                member.Tolerance,
                mode: member.Mode,
                encoding: "future-wire",
                typeShape: member.TypeShape);

            var exception = Assert.Throws<InvalidOperationException>(
                () => FoxgloveSourceEmitter.EmitClass(
                    "Demo",
                    "FutureEncodingPublisher",
                    new[] { member }));
            Assert.Contains(
                "encoding",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GeneratedPayloadsCoverUnityOrderNestedEnumNullableListBinaryAndFailureCleanup()
        {
            var nestedShape = FoxRunTypeShape.Object(
                "Demo.Nested",
                new[]
                {
                    new FoxRunTypeField(
                        "Value",
                        "Value",
                        FoxRunTypeShape.Canonical("int32"))
                });
            var envelopeShape = FoxRunTypeShape.Object(
                "Demo.Envelope",
                new[]
                {
                    new FoxRunTypeField(
                        "Mode",
                        "Mode",
                        FoxRunTypeShape.Enum(
                            "Demo.Mode",
                            new[]
                            {
                                new FoxRunEnumValue("Idle", -1),
                                new FoxRunEnumValue("Active", 2),
                                new FoxRunEnumValue("Running", 2)
                            })),
                    new FoxRunTypeField("Nested", "Nested", nestedShape),
                    new FoxRunTypeField(
                        "Optional",
                        "Optional",
                        FoxRunTypeShape.Canonical("int32", nullable: true)),
                    new FoxRunTypeField(
                        "Payload",
                        "Payload",
                        FoxRunTypeShape.Collection(
                            FoxRunCollectionKind.Binary,
                            FoxRunTypeShape.Canonical("uint8"))),
                    new FoxRunTypeField(
                        "Samples",
                        "Samples",
                        FoxRunTypeShape.Collection(
                            FoxRunCollectionKind.List,
                            FoxRunTypeShape.Canonical("int32")))
                });
            var topics = new[]
            {
                "/phase185/color",
                "/phase185/envelope",
                "/phase185/quaternion",
                "/phase185/text",
                "/phase185/vector2",
                "/phase185/vector3"
            };
            var typeNames = new[]
            {
                "UnityEngine.Color",
                "Demo.Envelope",
                "UnityEngine.Quaternion",
                "System.String",
                "UnityEngine.Vector2",
                "UnityEngine.Vector3"
            };
            var shapes = new[]
            {
                FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Color)),
                envelopeShape,
                FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Quaternion)),
                FoxRunTypeShape.Canonical("string", nullable: true),
                FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Vector2)),
                FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Vector3))
            };
            var topicMap = new Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>>();
            for (var index = 0; index < topics.Length; index++)
            {
                topicMap[topics[index]] = new List<FoxgloveSourceEmitter.TopicMember>
                {
                    new FoxgloveSourceEmitter.TopicMember(
                        "_value",
                        typeNames[index],
                        topics[index],
                        10f,
                        typeNames[index],
                        (int)FoxRunPolicy.FixedRate,
                        0f,
                        mode: (int)FoxRunFlow.Publish,
                        encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                        typeShape: shapes[index])
                };
            }
            var generated = new StringBuilder();
            MessagePackPublishDispatchEmitter.EmitFieldsAndBuilders(
                generated,
                topics,
                topicMap,
                "    ");
            var captureFields = new StringBuilder();
            for (var index = 0; index < topics.Length; index++)
            {
                captureFields.AppendLine(
                    "        private "
                    + typeNames[index]
                    + " __foxRunCapture_"
                    + index
                    + "_0;");
            }
            var declaration = @"
using System.Collections.Generic;

namespace Demo
{
    public enum Mode { Idle = -1, Active = 2, Running = 2 }
    public sealed class Nested { public int Value; }
    public sealed class Envelope
    {
        public Mode Mode;
        public Nested Nested;
        public int? Optional;
        public byte[] Payload;
        public List<int> Samples;
    }

    public sealed class MessagePackBehavior
    {
"
                + captureFields
                + generated
                + @"
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185GeneratedMessagePack_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(declaration) },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);

            Assert.True(
                emit.Success,
                "Generated MessagePack fixture failed to compile: "
                + string.Join("; ", emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var type = assembly.GetType("Demo.MessagePackBehavior", throwOnError: true);
            var instance = Activator.CreateInstance(type);
            CaptureField(type, 4).SetValue(
                instance,
                new UnityEngine.Vector2 { x = 1f, y = 2f });
            CaptureField(type, 5).SetValue(
                instance,
                new UnityEngine.Vector3 { x = 1f, y = 2f, z = 3f });
            CaptureField(type, 2).SetValue(
                instance,
                new UnityEngine.Quaternion { x = 1f, y = 2f, z = 3f, w = 4f });
            CaptureField(type, 0).SetValue(
                instance,
                new UnityEngine.Color { r = 1f, g = 2f, b = 3f, a = 4f });

            var nestedType = assembly.GetType("Demo.Nested", throwOnError: true);
            var nested = Activator.CreateInstance(nestedType);
            nestedType.GetField("Value")!.SetValue(nested, 7);
            var envelopeType = assembly.GetType("Demo.Envelope", throwOnError: true);
            var envelope = Activator.CreateInstance(envelopeType);
            envelopeType.GetField("Mode")!.SetValue(
                envelope,
                Enum.ToObject(assembly.GetType("Demo.Mode", throwOnError: true), 2));
            envelopeType.GetField("Nested")!.SetValue(envelope, nested);
            envelopeType.GetField("Optional")!.SetValue(envelope, null);
            envelopeType.GetField("Payload")!.SetValue(
                envelope,
                new byte[] { 0xaa, 0xbb });
            envelopeType.GetField("Samples")!.SetValue(
                envelope,
                new List<int> { 1, 2 });
            CaptureField(type, 1).SetValue(instance, envelope);
            CaptureField(type, 3).SetValue(
                instance,
                "\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9"
                + "\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9");

            var expected = new Dictionary<string, byte[]>
            {
                ["/phase185/vector2"] = new byte[]
                {
                    0x81, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65,
                    0x82, 0xa1, 0x78, 0xca, 0x3f, 0x80, 0x00, 0x00,
                    0xa1, 0x79, 0xca, 0x40, 0x00, 0x00, 0x00
                },
                ["/phase185/vector3"] = new byte[]
                {
                    0x81, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65,
                    0x83, 0xa1, 0x78, 0xca, 0x3f, 0x80, 0x00, 0x00,
                    0xa1, 0x79, 0xca, 0x40, 0x00, 0x00, 0x00,
                    0xa1, 0x7a, 0xca, 0x40, 0x40, 0x00, 0x00
                },
                ["/phase185/quaternion"] = new byte[]
                {
                    0x81, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65,
                    0x84, 0xa1, 0x78, 0xca, 0x3f, 0x80, 0x00, 0x00,
                    0xa1, 0x79, 0xca, 0x40, 0x00, 0x00, 0x00,
                    0xa1, 0x7a, 0xca, 0x40, 0x40, 0x00, 0x00,
                    0xa1, 0x77, 0xca, 0x40, 0x80, 0x00, 0x00
                },
                ["/phase185/color"] = new byte[]
                {
                    0x81, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65,
                    0x84, 0xa1, 0x72, 0xca, 0x3f, 0x80, 0x00, 0x00,
                    0xa1, 0x67, 0xca, 0x40, 0x00, 0x00, 0x00,
                    0xa1, 0x62, 0xca, 0x40, 0x40, 0x00, 0x00,
                    0xa1, 0x61, 0xca, 0x40, 0x80, 0x00, 0x00
                },
                ["/phase185/envelope"] = new byte[]
                {
                    0x81, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65,
                    0x85,
                    0xa4, 0x4d, 0x6f, 0x64, 0x65, 0x02,
                    0xa6, 0x4e, 0x65, 0x73, 0x74, 0x65, 0x64,
                    0x81, 0xa5, 0x56, 0x61, 0x6c, 0x75, 0x65, 0x07,
                    0xa8, 0x4f, 0x70, 0x74, 0x69, 0x6f, 0x6e, 0x61, 0x6c, 0xc0,
                    0xa7, 0x50, 0x61, 0x79, 0x6c, 0x6f, 0x61, 0x64,
                    0xc4, 0x02, 0xaa, 0xbb,
                    0xa7, 0x53, 0x61, 0x6d, 0x70, 0x6c, 0x65, 0x73,
                    0x92, 0x01, 0x02
                },
                ["/phase185/text"] = new byte[]
                {
                    0x81, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65,
                    0xd9, 0x20,
                    0xc3, 0xa9, 0xc3, 0xa9, 0xc3, 0xa9, 0xc3, 0xa9,
                    0xc3, 0xa9, 0xc3, 0xa9, 0xc3, 0xa9, 0xc3, 0xa9,
                    0xc3, 0xa9, 0xc3, 0xa9, 0xc3, 0xa9, 0xc3, 0xa9,
                    0xc3, 0xa9, 0xc3, 0xa9, 0xc3, 0xa9, 0xc3, 0xa9
                }
            };

            for (var index = 0; index < topics.Length; index++)
            {
                var topic = topics[index];
                if (!expected.TryGetValue(topic, out var bytes))
                    continue;
                var payload = Assert.IsType<byte[]>(
                    type.GetMethod(
                        "__BuildFoxRunMessagePack_" + index,
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(instance, null));
                Assert.Equal(bytes, payload);
            }

            var activePayload = Assert.IsType<byte[]>(
                type.GetMethod(
                        "__BuildFoxRunMessagePack_1",
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(instance, null));
            envelopeType.GetField("Mode")!.SetValue(
                envelope,
                Enum.Parse(
                    assembly.GetType("Demo.Mode", throwOnError: true),
                    "Running"));
            var aliasPayload = Assert.IsType<byte[]>(
                type.GetMethod(
                        "__BuildFoxRunMessagePack_1",
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(instance, null));
            Assert.Equal(activePayload, aliasPayload);

            envelopeType.GetField("Mode")!.SetValue(
                envelope,
                Enum.ToObject(
                    assembly.GetType("Demo.Mode", throwOnError: true),
                    3));
            var undeclaredEnum = Assert.Throws<TargetInvocationException>(
                () => type.GetMethod(
                        "__BuildFoxRunMessagePack_1",
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(instance, null));
            var enumError = Assert.IsType<InvalidOperationException>(
                undeclaredEnum.InnerException);
            Assert.Contains(
                "declared enum",
                enumError.Message,
                StringComparison.OrdinalIgnoreCase);

            CaptureField(type, 3).SetValue(instance, "\ud800");
            var failure = Assert.Throws<TargetInvocationException>(
                () => type.GetMethod(
                        "__BuildFoxRunMessagePack_3",
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(instance, null));
            Assert.IsType<EncoderFallbackException>(failure.InnerException);
            Assert.Contains("var __count_", generated.ToString(), StringComparison.Ordinal);
            Assert.Contains("WriteArrayHeader(__count_", generated.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynAndReflectionLoweringEmitTheSameMessagePackSource()
        {
            var shape = FoxRunTypeShape.Object(
                "Demo.Payload",
                new[]
                {
                    new FoxRunTypeField(
                        "value",
                        "Value",
                        FoxRunTypeShape.Canonical("int32"))
                });
            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "ParitySource", "_payload", "field",
                    "Demo.Payload", "global::Demo.Payload",
                    false, false, "", "/phase185/parity", "Demo.Payload",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Publish,
                    encoding: (int)FoxRunEncoding.MessagePack,
                    typeShape: shape,
                    namedArgumentPresence: FoxRunNamedArgumentPresence.Encoding)
            });
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo", "ParitySource", "_payload", "field",
                    "Demo.Payload", "global::Demo.Payload",
                    false, false, "", "/phase185/parity", "Demo.Payload",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Publish,
                    encoding: (int)FoxRunEncoding.MessagePack,
                    typeShape: shape,
                    namedArgumentPresence: FoxRunNamedArgumentPresence.Encoding)
            });

            var roslynSource = FoxgloveSourceEmitter.EmitClass(roslyn.Types.Single());
            var reflectionSource = FoxgloveSourceEmitter.EmitClass(reflection.Types.Single());

            Assert.Equal(roslynSource, reflectionSource);
            Assert.Contains("msgpack", roslynSource, StringComparison.Ordinal);
        }

        private static FoxgloveSourceEmitter.TopicMember Member(
            string memberName,
            string typeName,
            FoxRunTypeShape shape)
            => new(
                memberName,
                typeName,
                "/phase185/messagepack",
                10f,
                "Demo.MessagePack",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape: shape);

        private static string Slice(string source, string start, string end)
        {
            var startIndex = source.IndexOf(start, StringComparison.Ordinal);
            Assert.True(startIndex >= 0, "Missing generated start marker: " + start);
            var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
            Assert.True(endIndex > startIndex, "Missing generated end marker: " + end);
            return source.Substring(startIndex, endIndex - startIndex);
        }

        private static void AssertInOrder(string source, params string[] fragments)
        {
            var previous = -1;
            foreach (var fragment in fragments)
            {
                var current = source.IndexOf(fragment, StringComparison.Ordinal);
                Assert.True(current > previous, "Expected generated fragment in order: " + fragment);
                previous = current;
            }
        }

        private static MetadataReference[] DynamicCompilationReferences()
        {
            var locations = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                             ?? string.Empty)
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Append(typeof(FoxRunEncoding).Assembly.Location)
                .Append(typeof(Google.Protobuf.IMessage).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return locations
                .Select(location => MetadataReference.CreateFromFile(location))
                .ToArray();
        }

        private static Assembly CompileGenerated(string declaration)
        {
            var compilation = CSharpCompilation.Create(
                "Phase185FGeneratedMessagePack_"
                + Guid.NewGuid().ToString("N"),
                GeneratedPublishSyntaxTrees(declaration),
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver =
                CSharpGeneratorDriver.Create(
                    new FoxgloveLogSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var output,
                out _);
            using var image = new MemoryStream();
            var emit = output.Emit(image);
            Assert.True(
                emit.Success,
                "Generated MessagePack publisher failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(
                        diagnostic => diagnostic.ToString())));
            image.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(image);
        }

        private static SyntaxTree[] GeneratedPublishSyntaxTrees(
            params string[] sources)
        {
            var root = FindRepoRoot();
            return sources
                .Concat(new[]
                {
                    File.ReadAllText(Path.Combine(
                        root,
                        "Packages",
                        "dev.unity2foxglove.sdk",
                        "Tests",
                        "AdapterCompileStubs",
                        "FoxgloveLogHubCompileStubs.cs")),
                    GeneratedPublishInterfaces
                })
                .Select(source => CSharpSyntaxTree.ParseText(source))
                .ToArray();
        }

        private const string GeneratedPublishInterfaces = @"
namespace Unity.FoxgloveSDK.Components
{
    public interface IFoxgloveLogSource
    {
        int FoxgloveLog_TopicCount { get; }
        FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index);
        void FoxgloveLog_Publish(
            int topicIndex,
            FoxgloveManager manager,
            ulong nowNs);
    }

    public interface IFoxgloveTopicContractSource
    {
        string FoxgloveLog_Origin { get; }
        FoxTopicContract FoxgloveLog_GetContract(int index);
    }

    public interface IFoxgloveTopicBusSource
    {
        void FoxgloveLog_PublishToBus(
            int topicIndex,
            FoxTopicBus bus,
            ulong nowNs);
    }

    public interface IFoxgloveTopicBusDemandSource
    {
        bool FoxgloveLog_HasBusSubscribers(
            int topicIndex,
            FoxTopicBus bus);
    }

    public interface IFoxgloveTopicObserverSource
    {
        bool FoxgloveLog_HasObservers(
            int topicIndex,
            FoxTopicBus bus);
        void FoxgloveLog_PublishCapturedToObservers(
            int topicIndex,
            FoxTopicBus bus,
            ulong nowNs);
    }

    public interface IFoxgloveTopicSinkSource
    {
        void FoxgloveLog_PublishToSinks(
            int topicIndex,
            FoxTopicSinkRouter router,
            ulong nowNs);
    }

    public interface IFoxglovePublishCaptureSource
    {
        bool FoxgloveLog_BeginCapture(int topicIndex);
        void FoxgloveLog_EndCapture(int topicIndex);
    }

    public interface IFoxglovePublishTargetSource
    {
        bool FoxgloveLog_IsTargetReady(
            int topicIndex,
            FoxRunEndpoint target,
            FoxRunResolvedPublishContract contract,
            FoxgloveManager manager,
            FoxTopicBus bus,
            FoxTopicSinkRouter router,
            out string reason);
        bool FoxgloveLog_PublishCaptured(
            int topicIndex,
            FoxRunEndpoint target,
            FoxRunResolvedPublishContract contract,
            FoxgloveManager manager,
            FoxTopicBus bus,
            FoxTopicSinkRouter router,
            ulong nowNs,
            out string reason);
    }

    public interface IFoxglovePublishRecordingSource
    {
        bool FoxgloveLog_IsRecordingReady(
            int topicIndex,
            FoxRunResolvedPublishContract contract,
            FoxgloveManager manager,
            out string reason);
        bool FoxgloveLog_RecordCaptured(
            int topicIndex,
            FoxRunResolvedPublishContract contract,
            FoxgloveManager manager,
            ulong nowNs,
            out string reason);
    }

    public interface IFoxglovePublishOriginSource
    {
        bool FoxgloveLog_CanPublishOrigin(
            int topicIndex,
            bool explicitTrigger);
    }

    public interface IFoxgloveLogPolicySource
    {
        bool FoxgloveLog_ShouldPublish(
            int topicIndex,
            double nowSeconds);
        void FoxgloveLog_MarkPublished(
            int topicIndex,
            double nowSeconds);
    }
}";

        private static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(
                        directory.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Unity2Foxglove repository root.");
        }

        private static int FindTopicIndex(object source, string topic)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveLogSource");
            var count = Assert.IsType<int>(
                interfaceType.GetProperty("FoxgloveLog_TopicCount")!
                    .GetValue(source));
            var getTopic = interfaceType.GetMethod("FoxgloveLog_GetTopic")
                           ?? throw new InvalidOperationException(
                               "Generated log source is missing FoxgloveLog_GetTopic.");
            for (var index = 0;
                 index < count;
                 index++)
            {
                var info = getTopic.Invoke(source, new object[] { index })
                           ?? throw new InvalidOperationException(
                               "Generated topic metadata is null.");
                var actualTopic = Assert.IsType<string>(
                    info.GetType().GetField("Topic")!.GetValue(info));
                if (string.Equals(
                        actualTopic,
                        topic,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
            throw new InvalidOperationException(
                "Generated topic is missing: " + topic);
        }

        private static bool BeginCapture(object source, int topicIndex)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxglovePublishCaptureSource");
            var method = interfaceType.GetMethod("FoxgloveLog_BeginCapture")
                         ?? throw new InvalidOperationException(
                             "Generated capture source is missing FoxgloveLog_BeginCapture.");
            return Assert.IsType<bool>(
                method.Invoke(source, new object[] { topicIndex }));
        }

        private static void EndCapture(object source, int topicIndex)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxglovePublishCaptureSource");
            var method = interfaceType.GetMethod("FoxgloveLog_EndCapture")
                         ?? throw new InvalidOperationException(
                             "Generated capture source is missing FoxgloveLog_EndCapture.");
            method.Invoke(source, new object[] { topicIndex });
        }

        private static FoxTopicContract GetContract(
            object source,
            int topicIndex)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicContractSource");
            var method = interfaceType.GetMethod("FoxgloveLog_GetContract")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing FoxgloveLog_GetContract.");
            return Assert.IsType<FoxTopicContract>(
                method.Invoke(source, new object[] { topicIndex }));
        }

        private static bool HasObservers(
            object source,
            int topicIndex,
            FoxTopicBus bus)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicObserverSource");
            var method = interfaceType.GetMethod("FoxgloveLog_HasObservers")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing FoxgloveLog_HasObservers.");
            return Assert.IsType<bool>(
                method.Invoke(
                    source,
                    new object[] { topicIndex, bus }));
        }

        private static void PublishCapturedToObservers(
            object source,
            int topicIndex,
            FoxTopicBus bus,
            ulong timestampNs)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicObserverSource");
            var method = interfaceType.GetMethod(
                             "FoxgloveLog_PublishCapturedToObservers")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing observer publication.");
            method.Invoke(
                source,
                new object[] { topicIndex, bus, timestampNs });
        }

        private static void PublishToBus(
            object source,
            int topicIndex,
            FoxTopicBus bus,
            ulong timestampNs)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicBusSource");
            var method = interfaceType.GetMethod("FoxgloveLog_PublishToBus")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing bus publication.");
            method.Invoke(
                source,
                new object[] { topicIndex, bus, timestampNs });
        }

        private static void PublishToSinks(
            object source,
            int topicIndex,
            FoxTopicSinkRouter router,
            ulong timestampNs)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicSinkSource");
            var method = interfaceType.GetMethod("FoxgloveLog_PublishToSinks")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing sink publication.");
            method.Invoke(
                source,
                new object[] { topicIndex, router, timestampNs });
        }

        private static void SetCaptureEncoding(
            Type type,
            object instance,
            int topicIndex,
            FoxRunEncoding encoding)
        {
            var field = type.GetField(
                            "__foxRunCaptureEncoding_" + topicIndex,
                            BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException(
                            "Generated capture encoding is missing.");
            field.SetValue(instance, encoding);
        }

        private static Type FindGeneratedInterface(object source, string name)
            => source.GetType()
                   .GetInterfaces()
                   .SingleOrDefault(candidate => candidate.Name == name)
               ?? throw new InvalidOperationException(
                   "Generated source is missing " + name + ".");

        private static FoxgloveMsgPackReader CapturedMessagePack(
            Type type,
            object instance,
            int topicIndex)
        {
            var payload = Assert.IsType<byte[]>(
                type.GetField(
                        "__foxRunLastMessagePack_" + topicIndex,
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(instance));
            return new FoxgloveMsgPackReader(
                payload,
                FoxgloveMsgPackReadLimits.ForPayloadBytes(4096));
        }

        private static FieldInfo CaptureField(Type type, int topicIndex)
            => type.GetField(
                   "__foxRunCapture_" + topicIndex + "_0",
                   BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException(
                   "Generated capture field is missing for topic " + topicIndex + ".");

        private sealed class GeneratedRecordingSink : IFoxTopicSink
        {
            public string Name => "generated-recording";
            public FoxTopicSinkCapabilities Capabilities =>
                FoxTopicSinkCapabilities.Test;
            public int PublishCalls { get; private set; }
            public FoxTopicContract RegisteredContract { get; private set; }
            public FoxTopicContract LastContract { get; private set; }
            public byte[] LastPayload { get; private set; }

            public void Register(FoxTopicContract contract)
            {
                RegisteredContract = contract;
            }

            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
            {
                PublishCalls++;
                LastContract = contract;
                LastPayload = payload;
            }

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class GeneratedLegacyTargetSink : IFoxTopicSink
        {
            public string Name => "generated-legacy-target";
            public FoxTopicSinkCapabilities Capabilities =>
                FoxTopicSinkCapabilities.External;
            public int PublishCalls { get; private set; }

            public void Register(FoxTopicContract contract)
            {
            }

            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
            {
                PublishCalls++;
            }

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class GeneratedTargetSink :
            IFoxTopicSink,
            IFoxTopicTargetSink
        {
            public string Name => "generated-target";
            public FoxTopicSinkCapabilities Capabilities =>
                FoxTopicSinkCapabilities.External;
            public FoxRunEndpoint Target => FoxRunEndpoint.Ros2Native;
            public int PublishCalls { get; private set; }

            public void Register(FoxTopicContract contract)
            {
            }

            public bool IsReady(
                FoxTopicContract contract,
                out string reason)
            {
                reason = string.Empty;
                return true;
            }

            public bool TryPublish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin,
                out string reason)
            {
                PublishCalls++;
                reason = string.Empty;
                return true;
            }

            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
            {
                PublishCalls++;
            }

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
