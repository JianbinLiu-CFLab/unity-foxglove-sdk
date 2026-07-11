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

    }
}
