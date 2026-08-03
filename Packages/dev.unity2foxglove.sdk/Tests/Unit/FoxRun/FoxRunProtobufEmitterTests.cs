// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Google.Protobuf.Reflection;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunProtobufEmitterTests
    {
        [Fact]
        public void ExplicitProtobufTopicEmitsDirectPublishAndInboundBranches()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_count",
                "System.Int32",
                "/phase175/count",
                10f,
                "Demo.Count",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                encoding: "protobuf",
                protobufFieldNumber: 17,
                typeShape: FoxRunTypeShape.Canonical("int32"));

            var source = FoxgloveSourceEmitter.EmitClass("Demo", "Counter", new[] { member });
            var expectedSchemaName = FoxRunProtobufContractBuilder.ResolveMessageFullName(
                "Demo.Count",
                "Demo.Counter",
                "/phase175/count");

            Assert.Contains("mgr.PublishProto(\"/phase175/count\", \"" + expectedSchemaName + "\"", source);
            Assert.Contains("__BuildFoxRunProtobuf_0", source);
            Assert.Contains("FoxRunInboundProtobuf.TryRead", source);
            Assert.DoesNotContain("FoxRunInboundJson.TryRead(payload, \"count\"", source);
        }

        [Fact]
        public void ExplicitJsonTopicKeepsItsEstablishedGeneratedPath()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_count",
                "System.Int32",
                "/phase175/json_count",
                10f,
                "Demo.Count",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                encoding: "json");

            var source = FoxgloveSourceEmitter.EmitClass("Demo", "Counter", new[] { member });

            Assert.Contains("mgr.PublishJson(\"/phase175/json_count\", \"Demo.Count\"", source);
            Assert.Contains("FoxRunInboundJson.TryRead", source);
            Assert.DoesNotContain("mgr.PublishProto(\"/phase175/json_count\"", source);
        }

        [Fact]
        public void InheritedTopicWithoutSchemaPublishesUsingItsDescriptorSchemaName()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_count",
                "System.Int32",
                "/phase175/implicit",
                10f,
                "",
                (int)FoxRunPolicy.FixedRate,
                0f,
                encoding: "inherit",
                typeShape: FoxRunTypeShape.Canonical("int32"));
            var expectedSchemaName = FoxRunProtobufContractBuilder.Build(
                new FoxRunProtobufContractInput(
                    "Demo.Counter",
                    "/phase175/implicit",
                    "",
                    new[] { new FoxRunProtobufFieldInput("count", "_count", "int32", false) }))
                .MessageFullName;

            var source = FoxgloveSourceEmitter.EmitClass("Demo", "Counter", new[] { member });

            Assert.Contains(
                "mgr.PublishProto(\"/phase175/implicit\", \"" + expectedSchemaName + "\"",
                source);
            var expectedFieldNumber = FoxRunProtobufFieldNumber.Resolve(
                "Demo.Counter|/phase175/implicit|" + expectedSchemaName + "|_count",
                0);
            Assert.Contains(
                "FoxRunProtobufWire.WriteInt32(__payload, " + expectedFieldNumber + ", __foxRunCapture_0_0)",
                source);
        }

        [Fact]
        public void ProtobufCollectionInputUsesTheDescriptorFieldNumber()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_samples",
                "int[]",
                "/phase175/samples",
                10f,
                "",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Subscribe,
                encoding: "protobuf",
                typeShape: FoxRunTypeShape.Canonical("int32"));
            var expectedSchemaName = FoxRunProtobufContractBuilder.ResolveMessageFullName(
                "",
                "Demo.InputProbe",
                "/phase175/samples");
            var expectedFieldNumber = FoxRunProtobufFieldNumber.Resolve(
                "Demo.InputProbe|/phase175/samples|" + expectedSchemaName + "|_samples",
                0);

            var source = FoxgloveSourceEmitter.EmitClass("Demo", "InputProbe", new[] { member });

            Assert.Contains("if (__field.Number != " + expectedFieldNumber + ") continue;", source);
        }

        [Fact]
        public void NestedDtoAndRepeatedFieldsEmitTransactionalProtobufReader()
        {
            var pose = FoxRunTypeShape.Object(
                "Demo.Pose",
                new[]
                {
                    new FoxRunTypeField("position", "Position", FoxRunTypeShape.Canonical("unity.vector3.float32"))
                });
            var telemetry = FoxRunTypeShape.Object(
                "Demo.Telemetry",
                new[]
                {
                    new FoxRunTypeField("pose", "Pose", pose),
                    new FoxRunTypeField("samples", "Samples", FoxRunTypeShape.Canonical("float32"), repeated: true)
                });
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_telemetry", "Demo.Telemetry", "/phase175/telemetry", 10f, "Demo.Telemetry",
                (int)FoxRunPolicy.FixedRate, 0f,
                mode: (int)FoxRunFlow.Subscribe, encoding: "protobuf", typeShape: telemetry);

            var source = FoxgloveSourceEmitter.EmitClass("Demo", "TelemetrySource", new[] { member });

            Assert.Contains("__TryReadFoxRunProtobufObject_0", source);
            Assert.Contains("TryReadRepeated", source);
            Assert.Contains("value = __value", source);
            Assert.DoesNotContain("not yet supported", source, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void NullableValuesAreOmittedInsteadOfPassedToScalarProtobufWriters()
        {
            var rootNullable = new FoxgloveSourceEmitter.TopicMember(
                "_optionalCount", "System.Nullable<System.Int32>", "/phase175/optional", 10f, "Demo.Optional",
                (int)FoxRunPolicy.FixedRate, 0f,
                encoding: "protobuf", typeShape: FoxRunTypeShape.Canonical("int32"));
            var nested = FoxRunTypeShape.Object(
                "Demo.OptionalPayload",
                new[]
                {
                    new FoxRunTypeField(
                        "optionalCount",
                        "OptionalCount",
                        FoxRunTypeShape.Canonical("int32"),
                        isNullable: true),
                    new FoxRunTypeField(
                        "samples",
                        "Samples",
                        FoxRunTypeShape.Canonical("int32"),
                        repeated: true,
                        isNullable: true)
                });
            var nestedNullable = new FoxgloveSourceEmitter.TopicMember(
                "_payload", "Demo.OptionalPayload", "/phase175/optional-payload", 10f, "Demo.OptionalPayload",
                (int)FoxRunPolicy.FixedRate, 0f, encoding: "protobuf", typeShape: nested);

            var source = FoxgloveSourceEmitter.EmitClass("Demo", "NullableSource", new[] { rootNullable, nestedNullable });

            Assert.Contains("if (__foxRunCapture_0_0.HasValue)", source);
            Assert.Contains("WriteInt32(__payload", source);
            Assert.Contains(", __foxRunCapture_0_0.Value);", source);
            Assert.Contains("if (__value.OptionalCount.HasValue)", source);
            Assert.Contains(", __value.OptionalCount.Value);", source);
            Assert.Contains("if (__item.HasValue)", source);
            Assert.Contains(", __item.Value);", source);
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void SameObjectShapeWithDifferentNestedMetadataGetsDistinctMatchingWritersAndReaders()
        {
            var shape = FoxRunTypeShape.Object(
                "Demo.TaggedPayload",
                new[]
                {
                    new FoxRunTypeField(
                        "value",
                        "Value",
                        FoxRunTypeShape.Canonical("int32"))
                });
            var firstMetadata = TaggedMetadata(7);
            var secondMetadata = TaggedMetadata(11);
            var first = TaggedMember(
                "_first",
                "/phase185/tagged-first",
                "Demo.TaggedFirst",
                shape,
                firstMetadata);
            var second = TaggedMember(
                "_second",
                "/phase185/tagged-second",
                "Demo.TaggedSecond",
                shape,
                secondMetadata);

            var source = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "TaggedSource",
                new[] { first, second });

            Assert.Equal(
                2,
                System.Text.RegularExpressions.Regex.Matches(
                    source,
                    "private static void __WriteFoxRunProtobufObject_").Count);
            Assert.Contains("WriteInt32(__nested, 7,", source, StringComparison.Ordinal);
            Assert.Contains("WriteInt32(__nested, 11,", source, StringComparison.Ordinal);
            Assert.Contains("case 7:", source, StringComparison.Ordinal);
            Assert.Contains("case 11:", source, StringComparison.Ordinal);

            Assert.Equal(
                7,
                NestedDescriptorFieldNumber(
                    "/phase185/tagged-first",
                    "Demo.TaggedFirst",
                    shape,
                    firstMetadata));
            Assert.Equal(
                11,
                NestedDescriptorFieldNumber(
                    "/phase185/tagged-second",
                    "Demo.TaggedSecond",
                    shape,
                secondMetadata));
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void SameObjectTypeRequiredAndNullableFieldsShareOneProtobufMessageDefinition()
        {
            var nested = FoxRunTypeShape.Object(
                "Demo.ReusedSample",
                new[]
                {
                    new FoxRunTypeField(
                        "value",
                        "Value",
                        FoxRunTypeShape.Canonical("int32"))
                });
            var root = FoxRunTypeShape.Object(
                "Demo.ReusedEnvelope",
                new[]
                {
                    new FoxRunTypeField(
                        "required",
                        "Required",
                        nested),
                    new FoxRunTypeField(
                        "optional",
                        "Optional",
                        nested.WithNullable(),
                        isNullable: true)
                });

            var contract = FoxRunProtobufContractBuilder.Build(
                new FoxRunProtobufContractInput(
                    "Demo.ReusedSource",
                    "/phase185/reused-nullable",
                    "Demo.ReusedEnvelope",
                    new[]
                    {
                        new FoxRunProtobufFieldInput(
                            "value",
                            "_value",
                            "Demo.ReusedEnvelope",
                            false,
                            typeShape: root)
                    }));
            var descriptor = FileDescriptorSet.Parser.ParseFrom(
                contract.FileDescriptorSet);
            var file = Assert.Single(descriptor.File);
            var envelope = Assert.Single(
                file.MessageType,
                message => string.Equals(
                    message.Name,
                    "Demo_ReusedEnvelope",
                    StringComparison.Ordinal));

            Assert.Equal(
                envelope.Field[0].TypeName,
                envelope.Field[1].TypeName);
            Assert.Single(
                file.MessageType,
                message => string.Equals(
                    message.Name,
                    "Demo_ReusedSample",
                    StringComparison.Ordinal));
        }



        [Fact]
        [Trait("Phase", "185-A")]
        public void UnityObjectShapeKeepsCanonicalProtobufPublishCodec()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_position",
                "UnityEngine.Vector3",
                "/phase185/vector3",
                10f,
                "Demo.Vector3",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                encoding: "protobuf",
                protobufFieldNumber: 17,
                typeShape: FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Vector3)));

            var source = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "VectorSource",
                new[] { member });

            Assert.Contains(
                "FoxRunProtobufWire.WriteVector3(__payload, 17, __foxRunCapture_0_0);",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "private static void __WriteFoxRunProtobufObject_",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void UnityObjectShapeKeepsCanonicalProtobufInputCodec()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_position",
                "UnityEngine.Vector3",
                "/phase185/vector3-input",
                10f,
                "Demo.Vector3",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Subscribe,
                encoding: "protobuf",
                protobufFieldNumber: 17,
                typeShape: FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Vector3)));

            var source = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "VectorInput",
                new[] { member });

            Assert.Contains(
                "FoxRunInboundProtobuf.TryRead(payload, 17, out global::UnityEngine.Vector3 __value, out error)",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "private static bool __TryReadFoxRunProtobufObject_",
                source,
                StringComparison.Ordinal);
        }

        private static FoxRunProtobufMetadata TaggedMetadata(int nestedFieldNumber)
            => new FoxRunProtobufMetadata(
                17,
                new FoxRunProtobufTypeMetadata(
                    "Demo.TaggedPayload",
                    new[]
                    {
                        new FoxRunProtobufFieldMetadata(
                            "Value",
                            "value",
                            nestedFieldNumber)
                    }));

        private static FoxgloveSourceEmitter.TopicMember TaggedMember(
            string memberName,
            string topic,
            string schemaName,
            FoxRunTypeShape shape,
            FoxRunProtobufMetadata metadata)
            => new FoxgloveSourceEmitter.TopicMember(
                memberName,
                "Demo.TaggedPayload",
                topic,
                10f,
                schemaName,
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                encoding: "protobuf",
                typeShape: shape,
                protobufMetadata: metadata);

        private static int NestedDescriptorFieldNumber(
            string topic,
            string schemaName,
            FoxRunTypeShape shape,
            FoxRunProtobufMetadata metadata)
        {
            var descriptor = FileDescriptorSet.Parser.ParseFrom(
                FoxRunProtobufContractBuilder.Build(
                    new FoxRunProtobufContractInput(
                        "Demo.TaggedSource",
                        topic,
                        schemaName,
                        new[]
                        {
                            new FoxRunProtobufFieldInput(
                                "payload",
                                "_payload",
                                "Demo.TaggedPayload",
                                false,
                                protobufFieldNumber: 17,
                                typeShape: shape,
                                protobufMetadata: metadata)
                        })).FileDescriptorSet);
            var nested = Assert.Single(
                Assert.Single(descriptor.File).MessageType,
                message => message.Name.EndsWith("TaggedPayload", StringComparison.Ordinal));
            return Assert.Single(nested.Field, field => field.Name == "value").Number;
        }

    }
}
