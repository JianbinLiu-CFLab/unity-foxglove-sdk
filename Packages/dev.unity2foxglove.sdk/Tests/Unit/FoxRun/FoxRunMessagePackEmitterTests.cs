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
        public void MessagePackLiveRecordingAndSinksConsumeTheCaptureCacheButRos2DoesNot()
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
                targets: FoxRunGenerationDescriptorConstants.FoxgloveTarget
                         + ","
                         + FoxRunGenerationDescriptorConstants.Ros2NativeTarget
                         + ","
                         + FoxRunGenerationDescriptorConstants.Ros2BridgeTarget);

            var source = FoxgloveSourceEmitter.EmitClass("Demo", "Counter", new[] { member });

            Assert.Contains("PublishFoxRunMessagePackBytes(", source, StringComparison.Ordinal);
            Assert.Contains("TryPublishFoxRunMessagePackRecording(", source, StringComparison.Ordinal);
            Assert.Contains(
                "router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0), FoxRunEncoding.MessagePack, nowNs, __foxRunLastMessagePack_0",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "FoxRun_PublishRos2Native_0(__foxRunLastMessagePack_0",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "FoxRun_PublishRos2Bridge_0(__foxRunLastMessagePack_0",
                source,
                StringComparison.Ordinal);
            Assert.Contains("msgpack", source, StringComparison.Ordinal);
        }

        [Fact]
        public void InheritedMessagePackRecordingOnlyFreezesResolvedEncodingBeforeCapture()
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
            var recordingReady = Slice(
                source,
                "bool IFoxglovePublishRecordingSource.FoxgloveLog_IsRecordingReady",
                "bool IFoxglovePublishRecordingSource.FoxgloveLog_RecordCaptured");
            var recordCaptured = Slice(
                source,
                "bool IFoxglovePublishRecordingSource.FoxgloveLog_RecordCaptured",
                "void IFoxgloveLogSource.FoxgloveLog_Publish");

            Assert.Contains(
                "__foxRunCaptureEncoding_0 = resolved.FoxgloveEncoding;",
                recordingReady,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (__foxRunCaptureEncoding_0 == FoxRunEncoding.MessagePack)",
                beginCapture,
                StringComparison.Ordinal);
            Assert.Contains(
                "TryPrepareFoxRunMessagePackRecording",
                recordingReady,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (__foxRunCaptureEncoding_0 == FoxRunEncoding.MessagePack)",
                recordCaptured,
                StringComparison.Ordinal);
            Assert.Contains(
                "TryPublishFoxRunMessagePackRecording",
                recordCaptured,
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
                            new[] { new FoxRunEnumValue("Idle", -1), new FoxRunEnumValue("Active", 2) })),
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
    public enum Mode { Idle = -1, Active = 2 }
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
            CaptureField(type, 3).SetValue(instance, "valid");

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
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return locations
                .Select(location => MetadataReference.CreateFromFile(location))
                .ToArray();
        }

        private static FieldInfo CaptureField(Type type, int topicIndex)
            => type.GetField(
                   "__foxRunCapture_" + topicIndex + "_0",
                   BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException(
                   "Generated capture field is missing for topic " + topicIndex + ".");
    }
}
