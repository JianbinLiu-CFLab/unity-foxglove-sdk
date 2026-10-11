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
    public sealed partial class FoxRunMessagePackEmitterTests
    {
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
            var enumError = Assert.IsType<ComponentPublisherSessionFailure>(
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
        public void GeneratedPayloadsBindMultiFieldCapturesByMemberAndEncodeUnsignedAndNullCollections()
        {
            var topics = new[] { "/phase190/binding", "/phase190/nulllist", "/phase190/unsigned" };
            var topicMap = new Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>>
            {
                // Member-name order (_zeta, alpha) differs from JSON-name order (alpha, zeta).
                ["/phase190/binding"] = new List<FoxgloveSourceEmitter.TopicMember>
                {
                    Phase190Member("_zeta", "System.Double", "/phase190/binding", FoxRunTypeShape.Canonical("float64")),
                    Phase190Member("alpha", "System.Int32", "/phase190/binding", FoxRunTypeShape.Canonical("int32"))
                },
                ["/phase190/nulllist"] = new List<FoxgloveSourceEmitter.TopicMember>
                {
                    Phase190Member(
                        "_value",
                        "System.Collections.Generic.List<int>",
                        "/phase190/nulllist",
                        FoxRunTypeShape.Collection(FoxRunCollectionKind.List, FoxRunTypeShape.Canonical("int32")))
                },
                ["/phase190/unsigned"] = new List<FoxgloveSourceEmitter.TopicMember>
                {
                    Phase190Member("_value", "System.UInt32", "/phase190/unsigned", FoxRunTypeShape.Canonical("uint32"))
                }
            };
            var generated = new StringBuilder();
            MessagePackPublishDispatchEmitter.EmitFieldsAndBuilders(generated, topics, topicMap, "    ");
            var declaration = @"
using System.Collections.Generic;

namespace Phase190
{
    public sealed class MessagePackBinding
    {
        private double __foxRunCapture_0_0;
        private int __foxRunCapture_0_1;
        private List<int> __foxRunCapture_1_0;
        private uint __foxRunCapture_2_0;
"
                + generated
                + @"
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase190GeneratedMessagePack_" + Guid.NewGuid().ToString("N"),
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
            var type = assembly.GetType("Phase190.MessagePackBinding", throwOnError: true);
            var instance = Activator.CreateInstance(type);
            const BindingFlags fieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            type.GetField("__foxRunCapture_0_0", fieldFlags)!.SetValue(instance, 1.5d);
            type.GetField("__foxRunCapture_0_1", fieldFlags)!.SetValue(instance, 7);
            type.GetField("__foxRunCapture_1_0", fieldFlags)!.SetValue(instance, null);
            type.GetField("__foxRunCapture_2_0", fieldFlags)!.SetValue(instance, 3_000_000_000u);

            var expected = new[]
            {
                // {"alpha": 7, "zeta": 1.5}
                new byte[]
                {
                    0x82,
                    0xa5, 0x61, 0x6c, 0x70, 0x68, 0x61, 0x07,
                    0xa4, 0x7a, 0x65, 0x74, 0x61, 0xcb, 0x3f, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                },
                // {"value": nil}
                new byte[] { 0x81, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65, 0xc0 },
                // {"value": uint32 3000000000}
                new byte[] { 0x81, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65, 0xce, 0xb2, 0xd0, 0x5e, 0x00 }
            };
            for (var index = 0; index < topics.Length; index++)
            {
                var payload = Assert.IsType<byte[]>(
                    type.GetMethod(
                            "__BuildFoxRunMessagePack_" + index,
                            BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(instance, null));
                Assert.Equal(expected[index], payload);
            }
        }

    }
}
