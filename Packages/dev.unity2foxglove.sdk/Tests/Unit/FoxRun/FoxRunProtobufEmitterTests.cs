// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

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
                0,
                0f,
                0f,
                mode: 2,
                encoding: "protobuf",
                protobufFieldNumber: 17,
                protobufTypeShape: FoxRunProtobufTypeShape.Canonical("int32"));

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
                0,
                0f,
                0f,
                mode: 2,
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
                0,
                0f,
                0f,
                encoding: "inherit",
                protobufTypeShape: FoxRunProtobufTypeShape.Canonical("int32"));
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
                "FoxRunProtobufWire.WriteInt32(__payload, " + expectedFieldNumber + ", this._count)",
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
                0,
                0f,
                0f,
                mode: 1,
                encoding: "protobuf",
                protobufTypeShape: FoxRunProtobufTypeShape.Canonical("int32"));
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
            var pose = FoxRunProtobufTypeShape.Object(
                "Demo.Pose",
                new[]
                {
                    new FoxRunProtobufTypeField("position", "Position", FoxRunProtobufTypeShape.Canonical("unity.vector3.float32"))
                });
            var telemetry = FoxRunProtobufTypeShape.Object(
                "Demo.Telemetry",
                new[]
                {
                    new FoxRunProtobufTypeField("pose", "Pose", pose),
                    new FoxRunProtobufTypeField("samples", "Samples", FoxRunProtobufTypeShape.Canonical("float32"), repeated: true)
                });
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_telemetry", "Demo.Telemetry", "/phase175/telemetry", 10f, "Demo.Telemetry",
                0, 0f, 0f, mode: 1, encoding: "protobuf", protobufTypeShape: telemetry);

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
                0, 0f, 0f, encoding: "protobuf", protobufTypeShape: FoxRunProtobufTypeShape.Canonical("int32"));
            var nested = FoxRunProtobufTypeShape.Object(
                "Demo.OptionalPayload",
                new[]
                {
                    new FoxRunProtobufTypeField(
                        "optionalCount",
                        "OptionalCount",
                        FoxRunProtobufTypeShape.Canonical("int32"),
                        isNullable: true),
                    new FoxRunProtobufTypeField(
                        "samples",
                        "Samples",
                        FoxRunProtobufTypeShape.Canonical("int32"),
                        repeated: true,
                        isNullable: true)
                });
            var nestedNullable = new FoxgloveSourceEmitter.TopicMember(
                "_payload", "Demo.OptionalPayload", "/phase175/optional-payload", 10f, "Demo.OptionalPayload",
                0, 0f, 0f, encoding: "protobuf", protobufTypeShape: nested);

            var source = FoxgloveSourceEmitter.EmitClass("Demo", "NullableSource", new[] { rootNullable, nestedNullable });

            Assert.Contains("if (this._optionalCount.HasValue)", source);
            Assert.Contains("WriteInt32(__payload", source);
            Assert.Contains(", this._optionalCount.Value);", source);
            Assert.Contains("if (__value.OptionalCount.HasValue)", source);
            Assert.Contains(", __value.OptionalCount.Value);", source);
            Assert.Contains("if (__item.HasValue)", source);
            Assert.Contains(", __item.Value);", source);
        }

    }
}
