// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Components;
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

            Assert.Contains("mgr.PublishProto(\"/phase175/count\", \"Demo.Count\"", source);
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
        public void ProtobufWireRoundTripsVectorWithoutJsonEnvelope()
        {
            var payload = new System.Collections.Generic.List<byte>();
            FoxRunProtobufWire.WriteVector3(payload, 17, new UnityEngine.Vector3 { x = 1f, y = -2f, z = 3.5f });

            Assert.True(FoxRunInboundProtobuf.TryRead(payload.ToArray(), 17, out UnityEngine.Vector3 value, out var error));
            Assert.Empty(error);
            Assert.Equal(1f, value.x);
            Assert.Equal(-2f, value.y);
            Assert.Equal(3.5f, value.z);
            Assert.DoesNotContain((byte)'{', payload);
        }

        [Fact]
        public void MalformedProtobufDoesNotProduceAnInboundValue()
        {
            var malformed = new byte[] { 0x88, 0x01, 0x80 };

            Assert.False(FoxRunInboundProtobuf.TryRead(malformed, 17, out int value, out var error));
            Assert.Equal(0, value);
            Assert.NotEmpty(error);
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
        public void ProtobufFieldReaderPreservesRepeatedFieldOccurrences()
        {
            var payload = new System.Collections.Generic.List<byte>();
            FoxRunProtobufWire.WriteFloat(payload, 4, 1.5f);
            FoxRunProtobufWire.WriteFloat(payload, 4, 2.5f);
            var fields = new System.Collections.Generic.List<FoxRunProtobufField>();

            Assert.True(FoxRunInboundProtobuf.TryReadFields(payload.ToArray(), fields, out var error));
            Assert.Empty(error);
            Assert.Equal(2, fields.Count);
            Assert.All(fields, field => Assert.Equal(4, field.Number));
        }

        [Fact]
        public void FieldDecoderRejectsWrongWireTypeWithoutProducingValue()
        {
            var field = new FoxRunProtobufField(4, 2, new byte[] { 1, 2, 3 });

            Assert.False(FoxRunInboundProtobuf.TryDecodeFloat(field, out var value, out var error));
            Assert.Equal(0f, value);
            Assert.Contains("wire type", error, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PackedRepeatedScalarsAndDefaultVectorComponentsDecode()
        {
            var packed = new FoxRunProtobufField(4, 2, new byte[] { 1, 0x96, 0x01 });
            var values = new System.Collections.Generic.List<int>();

            Assert.True(FoxRunInboundProtobuf.TryReadRepeatedInt32(packed, values, out var packedError));
            Assert.Empty(packedError);
            Assert.Equal(new[] { 1, 150 }, values);

            var vectorPayload = new System.Collections.Generic.List<byte>();
            FoxRunProtobufWire.WriteVector3(vectorPayload, 7, new UnityEngine.Vector3 { y = 2f });

            Assert.True(FoxRunInboundProtobuf.TryRead(vectorPayload.ToArray(), 7, out UnityEngine.Vector3 vector, out var vectorError));
            Assert.Empty(vectorError);
            Assert.Equal(0f, vector.x);
            Assert.Equal(2f, vector.y);
            Assert.Equal(0f, vector.z);
        }
    }
}
