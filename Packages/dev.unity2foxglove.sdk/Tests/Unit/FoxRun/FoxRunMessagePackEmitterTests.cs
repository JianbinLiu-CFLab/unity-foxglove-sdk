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
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    [Trait("Phase", "185-B")]
    [Trait("Domain", "FoxRun")]
    public sealed partial class FoxRunMessagePackEmitterTests
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
                "if (__foxRunCaptureEncoding_0 == FoxRunEncoding.MessagePack || __foxRunRecordMessagePack_0)",
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
        [Trait("Phase", "186-A")]
        public void RecordingOnlyMessagePackUsesAnIndependentCaptureDecision()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_count",
                "System.Int32",
                "/phase186/recording-codec",
                10f,
                "Demo.Count",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                encoding:
                    FoxRunGenerationDescriptorConstants.JsonEncoding,
                typeShape: FoxRunTypeShape.Canonical("int32"),
                publishTransportIds: new[]
                {
                    "unity2foxglove.r2fu"
                });

            var source = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "RecordingCounter",
                new[] { member });
            var beginCapture = Slice(
                source,
                "bool IFoxglovePublishCaptureSource.FoxgloveLog_BeginCapture",
                "void IFoxglovePublishCaptureSource.FoxgloveLog_EndCapture");
            var recording = Slice(
                source,
                "bool IFoxglovePublishRecordingSource.FoxgloveLog_IsRecordingReady",
                "[Preserve]");

            Assert.Contains(
                "private bool __foxRunRecordMessagePack_0;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (__foxRunRecordMessagePack_0)",
                beginCapture,
                StringComparison.Ordinal);
            Assert.Single(
                Regex.Matches(
                        beginCapture,
                        "__BuildFoxRunMessagePack_0\\(\\)")
                    .Cast<Match>());
            Assert.Contains(
                "TryPrepareFoxRunMessagePackRecording",
                recording,
                StringComparison.Ordinal);
            Assert.Contains(
                "TryPublishFoxRunMessagePackRecording",
                recording,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "__foxRunCaptureEncoding_0",
                recording,
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


    }
}
